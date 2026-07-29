# HB_Claude – Onboarding

MatchMate är ett system för att registrera handbollsmatchhändelser live och räkna
statistik. Det här projektet är en migrering av den gamla VB.NET Web Forms-appen
(`C:\GH\HB\HB`) till **C# ASP.NET Core 10 Razor Pages**.

- **Stack:** .NET 10, Razor Pages, Bootstrap, MySqlConnector, QuestPDF.
- **Databas:** `matchmate_se_db_V2` på mysql69.unoeuro.com (utf8mb4, InnoDB, engelska
  snake_case-namn, FK/index). Datalagret finns i `Services/ApiService.cs` och alias:ar
  nya kolumnnamn tillbaka till de gamla modellnamnen, så Razor-sidorna är oförändrade.
- **Huvudsida:** `Pages/Events.cshtml` – live-registrering av matchhändelser.

Kör lokalt: `dotnet run` → http://localhost:5224 (service worker fungerar på `localhost`
även utan HTTPS).

---

## PWA – installerbar app

Appen kan installeras på iPhone/Android via webbläsaren ("Lägg till på hemskärmen"),
utan App Store eller Apple Developer-konto.

**Filer:**
| Fil | Roll |
|-----|------|
| `wwwroot/manifest.webmanifest` | Namn, ikoner, färger, `display: standalone`, `start_url=/Events` |
| `wwwroot/sw.js` | Service worker (app-skal + offline-fallback) |
| `wwwroot/offline.html` | "Ingen anslutning"-sida för vanliga sidor |
| `wwwroot/icons/*` | App-ikoner (handbollstema, temafärg `#c8282a`) |
| `Pages/Shared/_Layout.cshtml` | Manifest-länk, Apple-meta-taggar, ikoner, SW-registrering |

**Service worker-strategi (`sw.js`):**
- **Navigeringar:** network-first. `/Events` cachas så sidan kan öppnas offline; övriga
  sidor faller tillbaka till `offline.html`.
- **Statiska assets:** stale-while-revalidate.
- **POST hanteras aldrig av SW** – kö/synk sköts helt i applogiken (fungerar därför även på
  iOS som saknar Background Sync).
- Bumpa `CACHE_VERSION` vid ändring i cachningslogiken.

**Att känna till:**
- **Kräver HTTPS i drift** för att SW/PWA ska aktiveras (localhost är undantaget).
- .NET 10 `MapStaticAssets` fingeravtryckar filnamn (`manifest.<hash>.webmanifest`), men de
  vanliga sökvägarna (`/sw.js`, `/icons/...`) serveras parallellt. SW registreras mot den
  stabila sökvägen `/sw.js` för att få scope `/`.

---

## Offline-registrering (hybrid)

**Grundidé:** Online fungerar Events-sidan precis som förut (serverrenderade form-POST).
Ett klientlager tar över **endast när nätet saknas**, köar allt lokalt och synkar när
uppkopplingen kommer tillbaka. Progressiv förbättring – utan JS/online är beteendet
oförändrat.

**Omfattning:** registrera händelser + klocka + start/stop/paus för en match vars
lag/spelare/eventtyper redan cachats medan man var online. Att skapa nya lag/matcher/spelare
kräver fortfarande nätverk.

### Nyckelinsikt
Händelsers klassificering (mål/avslut/tekniskt fel/räddning) härleds ur händelsens
**namntext**, och namnet byggs ihop på klientsidan. Offline köas därför bara namnet +
spelare/tid/fas/zon – servern slår själv upp `event_type_id` via namnet vid synk (precis
som `AddMultiHändelse3` alltid gjort).

### Dataflöde
1. **Cache (online):** `GET /Events?handler=OfflineBundle&lagId=…` → spelare, ej avslutade
   matcher och `event_type`-tabellen lagras i IndexedDB (`OnGetOfflineBundleAsync`).
2. **Offline:** `offline-events.js` fångar `mainForm`-submit i capture-fasen när
   `navigator.onLine === false`, `preventDefault` och köar en operation i IndexedDB.
   En enkel kö-vy + preliminär summering renderas lokalt.
3. **Synk:** vid `online`-event, sidladdning eller "Synka nu" → `POST /Events?handler=Sync`
   med `{ ops: [...] }` och antiforgery-token i headern `RequestVerificationToken`.
   Servern (`OnPostSyncAsync`) applicerar ops i ordning och returnerar bekräftade
   `clientId`. Klienten tömmer bara bekräftade ur kön; när kön är tom laddas sidan om så
   serverns kanoniska, formaterade tabell visas.

### IndexedDB (`offline-events.js`, databas `matchmate`)
- `refdata` (keyPath `lagId`) – cachad referensdata.
- `queue` (keyPath `clientId`) – köade ops, sorteras på `seq` (monotont löpnummer).
- `meta` – bl.a. `seq`-räknaren.

### Operationstyper (`SyncOp.Kind`)
- `event` → `AddMultiHändelse3` (registrerad händelse).
- `status` → `SetMatchStatus` (Pågående/Avslutad).
- `startmarker` → **no-op** på servern (se "Fallgropar").

### Idempotens (dubblettsäkerhet)
Varje event-op har ett `clientId` (uuid) som lagras i `game_event.client_event_id`
(nullbar **UNIQUE**-kolumn, migrering `db/migrate_client_event_id.sql`). Insert:en är
`INSERT … ON DUPLICATE KEY UPDATE client_event_id = client_event_id` → en omsynkad rad blir
en no-op i stället för dubblett. Online-inserts har `NULL` (matchar aldrig UNIQUE) och
påverkas inte.

### Matchklocka
Tickar i klienten och persisteras i `localStorage` (`mm_clock`) så den överlever reload och
offline. `Pages/Events.cshtml` exponerar `window.mmClock` (`seconds`, `status`, `start`,
`togglePaus`, `setStatus`) som offline-lagret styr klockan igenom.

---

## Målvaktshändelser (MV) – tvåstegsflöde

MV-händelser lagras som `event_type.text` = `"<typ>: <placering>"`, t.ex.
`"Räddning 6M: Nere mitten"`. Placering väljs i ett **3×3-rutnät** (Uppe/Mitten/Nere ×
vänster/mitten/höger) efter att man valt typ. UI:t + JS finns i `Pages/Events.cshtml`
(`setMvType` / `setMvHändelse` / `resetMvSelection`, `window.resetMvSelection`).

`AddMultiHändelse3` är **självläkande**: om ett namn saknas i `event_type` skapas raden
(`is_goal=0`) i stället för att lagras som "Okänt" (id=0). Insläppta mål räknas via
`text LIKE '%mål%'` i `GetMalvakt`, inte via `is_goal`.

---

## Filkarta (PWA/offline)

| Område | Fil |
|--------|-----|
| Klientlager (IndexedDB, kö, synk, namnbyggnad, rendering) | `wwwroot/js/offline-events.js` |
| Events-UI, klocka, MV-placering, offline-hooks | `Pages/Events.cshtml` |
| JSON-endpoints `OnGetOfflineBundleAsync` / `OnPostSyncAsync` | `Pages/Events.cshtml.cs` |
| `GetEventTypes`, idempotent + självläkande insert | `Services/ApiService.cs` |
| `EventType`, `SyncOp`, `SyncBatch`, `SyncResult` | `Models/AppModels.cs` |
| DB-migrering (client_event_id) | `db/migrate_client_event_id.sql` |
| Manifest / SW / offline-sida / ikoner | `wwwroot/` |

---

## Fallgropar

- **HTTPS krävs i drift** för PWA/SW (localhost undantaget).
- **Antiforgery:** synk-POST:en skickar token i headern `RequestVerificationToken`; token
  läses från det dolda `__RequestVerificationToken`-fältet (`@Html.AntiForgeryToken()`).
- **iOS Safari saknar Background Sync** – synk sker när appen öppnas/tas fram, inte i
  bakgrunden.
- **Startmarkören är borttagen:** gamla `Start` satte in en `game_event` med
  `SpelareID=0/HändelseID=0`, vilket bryter mot FK:n `player_id→player.id` i v2 (ingen
  `player.id=0`). Insert:en är borttagen ur `OnPostStartStop`; klockan återupptas ändå via
  `GetMaxTid`. `startmarker`-op i synk är no-op så ev. gamla köade markörer rensas.
- **Namn måste hållas i synk:** `buildName` i `offline-events.js` speglar `BuildHändelse` i
  `Events.cshtml.cs`. Ändrar du namnlogiken på ena stället, ändra på andra.
- **Offline-vyn är preliminär** (enkel kö + lokal räkning). Serverns kanoniska tabell och
  siffror laddas först efter lyckad synk.
