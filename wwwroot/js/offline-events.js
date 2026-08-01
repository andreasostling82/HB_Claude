// =====================================================================
//  MatchMate – offline-registrering för Events-sidan.
//
//  Online fungerar sidan som vanligt (serverrenderade form-POST).
//  När nätet saknas tar detta lager över: händelser, start/stop och
//  matchstatus köas i IndexedDB och synkas till servern när nätet
//  kommer tillbaka. Paus/Fortsätt är enbart lokalt (även online).
//
//  Kön töms bara för de operationer servern bekräftat (per clientId),
//  så ingen data går förlorad om synken avbryts.
// =====================================================================
(function () {
    'use strict';

    var DB_NAME = 'matchmate';
    var DB_VER = 1;
    var db = null;
    var ctx = null;       // { userId, lagId, matchId, matchStatus, token }
    var refdata = null;   // { players, matches, eventTypes } för ctx.lagId

    // Tekniska fel – måste matcha BuildHändelse i Events.cshtml.cs
    var TECH_FAULTS = ['Övertramp', 'Offensiv_stuermer', 'Felaktig_spärr', 'Fot',
        'Stegfel', 'Passmiss', 'Tappad_boll', 'Dubbelstuds', 'Övrigt_regelfel'];

    // ---------- IndexedDB (litet promise-wrapper, inga beroenden) ----------

    function openDb() {
        return new Promise(function (resolve, reject) {
            var r = indexedDB.open(DB_NAME, DB_VER);
            r.onupgradeneeded = function (e) {
                var d = e.target.result;
                if (!d.objectStoreNames.contains('refdata')) d.createObjectStore('refdata', { keyPath: 'lagId' });
                if (!d.objectStoreNames.contains('queue')) d.createObjectStore('queue', { keyPath: 'clientId' });
                if (!d.objectStoreNames.contains('meta')) d.createObjectStore('meta', { keyPath: 'k' });
            };
            r.onsuccess = function (e) { resolve(e.target.result); };
            r.onerror = function () { reject(r.error); };
        });
    }

    function store(name, mode) { return db.transaction(name, mode).objectStore(name); }

    function idbPut(name, val) {
        return new Promise(function (resolve, reject) {
            var r = store(name, 'readwrite').put(val);
            r.onsuccess = function () { resolve(); };
            r.onerror = function () { reject(r.error); };
        });
    }
    function idbGet(name, key) {
        return new Promise(function (resolve, reject) {
            var r = store(name, 'readonly').get(key);
            r.onsuccess = function () { resolve(r.result); };
            r.onerror = function () { reject(r.error); };
        });
    }
    function idbAll(name) {
        return new Promise(function (resolve, reject) {
            var r = store(name, 'readonly').getAll();
            r.onsuccess = function () { resolve(r.result || []); };
            r.onerror = function () { reject(r.error); };
        });
    }
    function idbDel(name, key) {
        return new Promise(function (resolve, reject) {
            var r = store(name, 'readwrite').delete(key);
            r.onsuccess = function () { resolve(); };
            r.onerror = function () { reject(r.error); };
        });
    }

    // Monotont löpnummer så köordningen (registreringsordningen) bevaras.
    function nextSeq() {
        return idbGet('meta', 'seq').then(function (row) {
            var n = (row && row.v ? row.v : 0) + 1;
            return idbPut('meta', { k: 'seq', v: n }).then(function () { return n; });
        });
    }

    function uuid() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'id-' + Date.now() + '-' + Math.random().toString(16).slice(2);
    }

    // ---------- Namn & klassificering (speglar servern) ----------

    function buildName(rawTyp, position, fasVal, zonVal) {
        var isMV = position === 'MV';
        var namn;
        if (isMV) {
            namn = rawTyp;                                   // MV-händelser är redan namngivna
        } else if (TECH_FAULTS.indexOf(rawTyp) !== -1) {
            namn = 'TeknisktFel_' + rawTyp;
        } else if (rawTyp === 'Assist') {
            namn = rawTyp;
        } else {
            var p = fasVal === '1' ? 'Uppst' : (fasVal === '2' ? 'Fas1' : 'Fas2');
            namn = p + '_' + rawTyp;
        }
        var zon = '0';
        if (namn.indexOf('TeknisktFel') === -1 && namn.indexOf('Straff') === -1 && !isMV) zon = zonVal;
        return { namn: namn, zon: zon, fas: isMV ? '0' : fasVal };
    }

    // Preliminära offline-flaggor för summeringen (servern räknar om exakt efter synk).
    function computeFlags(rawTyp, namn, position) {
        if (position === 'MV') {
            return { goal: false, shot: false, fault: false, save: /räddning/i.test(namn), mvGoal: /mål/i.test(namn) };
        }
        var fault = namn.indexOf('TeknisktFel') !== -1;
        var shot = /(mål|_6m|_9m|Genombrott|Utanför|Räddning|Skott_i_täcket)/i.test(namn);
        var goal = false;
        if (refdata && refdata.eventTypes) {
            var t = refdata.eventTypes.find(function (x) { return x.text === namn; });
            if (t) goal = !!t.isGoal;
        }
        if (!goal) goal = ['_6m', '_9m', 'Genombrott', '_Straff_Mål_'].indexOf(rawTyp) !== -1;
        return { goal: goal, shot: shot, fault: fault, save: false, mvGoal: false };
    }

    function mmss(totalSeconds) {
        var s = parseInt(totalSeconds, 10) || 0;
        var m = Math.floor(s / 60), r = s % 60;
        return String(m).padStart(2, '0') + ':' + String(r).padStart(2, '0');
    }

    // ---------- Referensdata / cache ----------

    function loadRefFromCache() {
        if (!ctx.lagId) return Promise.resolve(null);
        return idbGet('refdata', ctx.lagId).then(function (row) { refdata = row || null; return refdata; });
    }

    function refreshBundle() {
        if (!ctx.lagId || !navigator.onLine) return Promise.resolve();
        return fetch('/Events?handler=OfflineBundle&lagId=' + encodeURIComponent(ctx.lagId), {
            headers: { 'Accept': 'application/json' }, credentials: 'same-origin'
        }).then(function (res) {
            if (!res.ok) throw new Error('bundle ' + res.status);
            return res.json();
        }).then(function (data) {
            refdata = data;
            return idbPut('refdata', data);
        }).catch(function () { /* offline eller fel – behåll cache */ });
    }

    // ---------- Kö ----------

    function currentQueue() {
        return idbAll('queue').then(function (all) {
            return all.sort(function (a, b) { return a.seq - b.seq; });
        });
    }

    function enqueue(op) {
        return nextSeq().then(function (seq) {
            op.seq = seq;
            op.clientId = op.clientId || uuid();
            op.ts = Date.now();
            return idbPut('queue', op).then(function () { return op; });
        });
    }

    // ---------- Rendering (offline-fältet + kö-listan + summering) ----------

    function fieldQueueForMatch(queue) {
        return queue.filter(function (o) {
            return (o.kind === 'event' || o.kind === 'spelstopp') && o.matchId === ctx.matchId;
        });
    }

    function renderBar(queue, state, msg) {
        var bar = document.getElementById('mmOfflineBar');
        if (!bar) return;
        var pending = queue.length;
        var offline = !navigator.onLine;
        var cls, text;

        if (state === 'syncing') { cls = 'alert-info'; text = 'Synkar ' + pending + ' händelser…'; }
        else if (state === 'error') { cls = 'alert-danger'; text = msg || 'Synk misslyckades – försök igen när du har nät.'; }
        else if (offline) { cls = 'alert-warning'; text = 'Offline – ' + pending + ' händelse' + (pending === 1 ? '' : 'r') + ' i kö (synkas när nätet kommer tillbaka).'; }
        else if (pending > 0) { cls = 'alert-info'; text = pending + ' händelser väntar på synk.'; }
        else { bar.innerHTML = ''; return; }

        var btn = (pending > 0 && navigator.onLine && state !== 'syncing')
            ? '<button type="button" class="btn btn-sm btn-primary ms-2" id="mmSyncBtn">Synka nu</button>' : '';
        bar.innerHTML = '<div class="alert ' + cls + ' py-1 mb-1 small d-flex align-items-center justify-content-between">'
            + '<span>' + text + '</span>' + btn + '</div>';
        var b = document.getElementById('mmSyncBtn');
        if (b) b.addEventListener('click', function () { sync(true); });
    }

    function renderSummary(events) {
        var el = document.getElementById('mmQueueSummary');
        if (!el) return;
        if (!events.length) { el.innerHTML = ''; return; }
        var goals = 0, shots = 0, faults = 0, saves = 0, mvGoals = 0, stopp = 0;
        events.forEach(function (o) {
            if (o.kind === 'spelstopp') { stopp++; return; }
            var f = o.flags || {};
            if (f.goal) goals++; if (f.shot) shots++; if (f.fault) faults++;
            if (f.save) saves++; if (f.mvGoal) mvGoals++;
        });
        var avslutPct = shots > 0 ? Math.round(goals / shots * 100) : 0;
        var raddPct = (mvGoals + saves) > 0 ? Math.round(saves / (mvGoals + saves) * 100) : 0;
        el.innerHTML = '<div class="alert alert-secondary py-1 mb-1 small">Preliminärt (offline) – '
            + 'Mål: ' + goals + ' - ' + mvGoals + '  Avslut: ' + shots + ' (' + avslutPct + '%)'
            + '  Räddningar: ' + saves + ' (' + raddPct + '%)  Fel: ' + faults + '  Spelstopp: ' + stopp + '</div>';
    }

    function renderQueue(queue) {
        var host = document.getElementById('mmQueue');
        if (!host) return;
        var events = fieldQueueForMatch(queue).slice().sort(function (a, b) {
            var ta = parseInt(a.tids, 10) || 0, tb = parseInt(b.tids, 10) || 0;
            return tb - ta || b.seq - a.seq;
        });
        renderSummary(events);
        if (!events.length) { host.innerHTML = ''; return; }

        var rows = events.map(function (o) {
            var f = o.flags || {};
            var spelare = (o.nummer || '') + ' ' + (o.efternamn || '');
            return '<tr class="table-warning">'
                + '<td>' + mmss(o.tids) + '</td>'
                + '<td>' + escapeHtml(spelare.trim()) + '</td>'
                + '<td>' + escapeHtml(o.label || o.handelsen) + ' <span class="badge bg-secondary">kö</span></td>'
                + '<td>' + (f.fault ? 'X' : '') + '</td>'
                + '<td>' + (o.zon && o.zon !== '0' ? o.zon : '') + '</td>'
                + '<td>' + (f.goal ? 'X' : '') + '</td>'
                + '<td>' + (f.shot ? 'X' : '') + '</td>'
                + '</tr>';
        }).join('');

        host.innerHTML = '<div class="table-responsive"><table class="table table-sm table-hover small">'
            + '<thead class="table-dark"><tr><th>Tid</th><th>Spelare</th><th>Händelse (väntar på synk)</th>'
            + '<th>T.Fel</th><th>Zon</th><th>Mål</th><th>Avslut</th></tr></thead><tbody>'
            + rows + '</tbody></table></div>';
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function refresh(state, msg) {
        return currentQueue().then(function (q) {
            renderBar(q, state, msg);
            renderQueue(q);
            return q;
        });
    }

    // ---------- Synk ----------

    var syncing = false;
    function sync(manual) {
        if (syncing || !navigator.onLine) return Promise.resolve();
        return currentQueue().then(function (ops) {
            if (!ops.length) return refresh();
            syncing = true;
            refresh('syncing');
            return fetch('/Events?handler=Sync', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': ctx.token },
                credentials: 'same-origin',
                body: JSON.stringify({ ops: ops })
            }).then(function (res) {
                if (!res.ok) throw new Error('sync ' + res.status);
                return res.json();
            }).then(function (data) {
                var confirmed = data.confirmed || [];
                return Promise.all(confirmed.map(function (id) { return idbDel('queue', id); }))
                    .then(function () { return currentQueue(); });
            }).then(function (remaining) {
                syncing = false;
                if (remaining.length === 0) {
                    // Allt synkat – ladda om så serverns kanoniska tabell/summering visas.
                    location.reload();
                } else {
                    refresh('error', 'En del händelser kunde inte synkas. Försök igen.');
                }
            }).catch(function () {
                syncing = false;
                refresh(manual ? 'error' : null);
            });
        });
    }

    // ---------- Offline-hantering av formulärsubmit ----------

    function handlerOf(submitter) {
        try {
            var fa = submitter && submitter.formAction ? submitter.formAction : '';
            if (!fa) return '';
            return new URL(fa, location.href).searchParams.get('handler') || '';
        } catch (e) { return ''; }
    }

    function val(id) { var el = document.getElementById(id); return el ? el.value : ''; }

    function showError(msg) { refresh('error', msg); }

    function handleOfflineSubmit(e) {
        var handler = handlerOf(e.submitter);

        // Byte av lag/match kräver servern – blockera offline med tydligt besked.
        if (handler !== 'Händelse' && handler !== 'StartStop' && handler !== 'PausFortsatt' && handler !== 'Spelstopp') {
            e.preventDefault();
            showError('Byte av lag/match kräver internet. Det du redan valt fungerar offline.');
            return;
        }
        e.preventDefault();

        if (!ctx.matchId) { showError('Ingen match vald! Välj match medan du har nät.'); return; }
        var seconds = window.mmClock ? String(window.mmClock.seconds()) : val('matchTidInput');

        if (handler === 'PausFortsatt') {
            if (window.mmClock) window.mmClock.togglePaus();
            refresh();
            return;
        }

        if (handler === 'StartStop') {
            var status = window.mmClock ? window.mmClock.status() : ctx.matchStatus;
            if (status === 'Pågående') {
                // Stopp
                if (window.mmClock) window.mmClock.setStatus('Avslutad');
                enqueue({ kind: 'status', matchId: ctx.matchId, status: 'Avslutad' }).then(function () { refresh(); });
            } else {
                // Start: bara statusbyte (ingen startmarkör – se serverns Sync-handler)
                if (window.mmClock) window.mmClock.start();
                enqueue({ kind: 'status', matchId: ctx.matchId, status: 'Pågående' })
                    .then(function () { refresh(); });
            }
            return;
        }

        if (handler === 'Spelstopp') {
            // Spelstopp kräver ingen spelare – köa som matchhändelse.
            enqueue({ kind: 'spelstopp', matchId: ctx.matchId, tids: seconds, label: 'Spelstopp' })
                .then(function () { refresh(); });
            return;
        }

        // handler === 'Händelse'
        var playerId = val('aktivSpelarInput');
        var position = val('aktivSpelarPosition');
        var rawTyp = val('handelseTypInput');
        if (!playerId) { showError('Ingen spelare vald!'); return; }

        var fasVal = val('fasValInput') || '1';
        var zonVal = val('zonValInput') || '0';
        var built = buildName(rawTyp, position, fasVal, zonVal);
        var flags = computeFlags(rawTyp, built.namn, position);

        // Spelaruppgifter för offline-visning (nummer + efternamn, som serverns tabell).
        var p = null;
        if (refdata && refdata.players) p = refdata.players.find(function (x) { return x.spId === playerId; });

        enqueue({
            kind: 'event',
            matchId: ctx.matchId,
            playerId: playerId,
            position: position,
            handelsen: built.namn,
            fas: built.fas,
            zon: built.zon,
            tids: seconds,
            label: (e.submitter && e.submitter.textContent ? e.submitter.textContent.trim() : built.namn),
            nummer: p ? p.nummer : '',
            efternamn: p ? p.efternamn : '',
            flags: flags
        }).then(function () {
            resetPlayerSelection();
            refresh();
        });
    }

    function resetPlayerSelection() {
        var a = document.getElementById('aktivSpelarInput'); if (a) a.value = '';
        var b = document.getElementById('aktivSpelarPosition'); if (b) b.value = '';
        document.querySelectorAll('.player-btn.active').forEach(function (el) { el.classList.remove('active'); });
        var zs = document.getElementById('zonSection'); if (zs) zs.style.display = 'none';
        var ms = document.getElementById('mvSection'); if (ms) ms.style.display = 'none';
        if (window.resetMvSelection) window.resetMvSelection();
    }

    // ---------- Init ----------

    function init(context) {
        ctx = context;
        if (!('indexedDB' in window)) return; // äldre webbläsare – kör vidare online-only
        openDb().then(function (d) {
            db = d;
            var form = document.getElementById('mainForm');
            if (form) {
                // Fångar submit i capture-fasen så vi hinner före den vanliga POST:en.
                form.addEventListener('submit', function (e) {
                    if (!navigator.onLine) handleOfflineSubmit(e);
                }, true);
            }
            window.addEventListener('online', function () { refresh(); sync(false); });
            window.addEventListener('offline', function () { refresh(); });

            return loadRefFromCache()
                .then(refresh)
                .then(function () { return refreshBundle(); })
                .then(function () { if (navigator.onLine) return sync(false); });
        }).catch(function (err) { console.warn('offline-events init:', err); });
    }

    window.MM = { init: init, sync: sync };
})();
