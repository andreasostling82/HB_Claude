-- =====================================================================
--  MatchMate – datamigrering  matchmate_se_db  ->  matchmate_se_db_V2
--  Körs EFTER schema_v2.sql. Ansluten till matchmate_se_db_V2;
--  källtabeller fullkvalificeras med matchmate_se_db.
--  Server-side INSERT...SELECT => MySQL konverterar latin1 -> utf8mb4.
--
--  Trasiga (föräldralösa) rader migreras inte utan arkiveras i
--  _orphan_*-tabeller så att ingenting går förlorat.
-- =====================================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ---------- Lookup-tabeller ----------

INSERT INTO phase (id, name)
SELECT FasID, Fas FROM matchmate_se_db.MM_Fas;

INSERT INTO zone (id, name)
SELECT ZonID, Zon FROM matchmate_se_db.MM_Zon;

INSERT INTO event_type (id, `text`, is_goal)
SELECT HL.HändelseID, HL.Text,
       CASE WHEN EXISTS (SELECT 1 FROM matchmate_se_db.MM_HändelseKategorier K
                         WHERE K.HändelseID = HL.HändelseID AND K.Kategori = 'Mål')
            THEN 1 ELSE 0 END
FROM matchmate_se_db.MM_HändelseLista HL;

-- ---------- Users (Password2 slopad, role = typ) ----------

INSERT INTO app_user (id, email, password_hash, role, status)
SELECT UserID, UserName, Password, COALESCE(typ, 1), status
FROM matchmate_se_db.MM_Users;

INSERT INTO user_delegate (user_id, delegate_user_id)
SELECT UU.userID, UU.useruserID
FROM matchmate_se_db.MM_UserUser UU
WHERE UU.userID      IN (SELECT id FROM app_user)
  AND UU.useruserID  IN (SELECT id FROM app_user);

-- ---------- Teams (inga föräldralösa) ----------

INSERT INTO team (id, name, series, home_venue, category, user_id)
SELECT Lag_ID, Namn, Serie, HemmaHall, KM, userID
FROM matchmate_se_db.MM_Lag;

-- ---------- Players (bara de vars lag finns; extranummer infällt) ----------

INSERT INTO player (id, team_id, first_name, last_name, position, shirt_number, alt_shirt_number)
SELECT S.SpelarID, S.LagID, S.Förnamn, S.Efternamn, S.Position, S.Nummer,
       (SELECT MAX(X.nummer) FROM matchmate_se_db.MM_XtraNummer X WHERE X.SpelarID = S.SpelarID)
FROM matchmate_se_db.MM_Spelare S
WHERE S.LagID IN (SELECT id FROM team);

-- ---------- Games (bara de vars lag finns) ----------

INSERT INTO game (id, team_id, played_on, opponent, venue, status)
SELECT M.MatchID, M.LagID, M.Datum, M.Motståndare, M.Plats, M.Status
FROM matchmate_se_db.MM_Matcher M
WHERE M.LagID IN (SELECT id FROM team);

-- ---------- Events (alla 5 FK-mål måste finnas; seconds = Tids/Tid) ----------

INSERT INTO game_event (game_id, player_id, event_type_id, phase_id, zone_id, seconds)
SELECT H.MatchID, H.SpelareID, H.HändelseID, H.Fas, H.Zon,
       COALESCE(H.Tids, TIME_TO_SEC(H.Tid))
FROM matchmate_se_db.MM_Händelser H
WHERE H.MatchID    IN (SELECT id FROM game)
  AND H.SpelareID  IN (SELECT id FROM player)
  AND H.HändelseID IN (SELECT id FROM event_type)
  AND H.Fas        IN (SELECT id FROM phase)
  AND H.Zon        IN (SELECT id FROM zone);

-- =====================================================================
--  Arkiv för föräldralösa rader (ingenting kastas)
-- =====================================================================

DROP TABLE IF EXISTS _orphan_player;
CREATE TABLE _orphan_player LIKE matchmate_se_db.MM_Spelare;
INSERT INTO _orphan_player
SELECT * FROM matchmate_se_db.MM_Spelare S
WHERE S.LagID NOT IN (SELECT Lag_ID FROM matchmate_se_db.MM_Lag);

DROP TABLE IF EXISTS _orphan_game;
CREATE TABLE _orphan_game LIKE matchmate_se_db.MM_Matcher;
INSERT INTO _orphan_game
SELECT * FROM matchmate_se_db.MM_Matcher M
WHERE M.LagID NOT IN (SELECT Lag_ID FROM matchmate_se_db.MM_Lag);

DROP TABLE IF EXISTS _orphan_xtranummer;
CREATE TABLE _orphan_xtranummer LIKE matchmate_se_db.MM_XtraNummer;
INSERT INTO _orphan_xtranummer
SELECT * FROM matchmate_se_db.MM_XtraNummer X
WHERE X.SpelarID NOT IN (
    SELECT SpelarID FROM matchmate_se_db.MM_Spelare
    WHERE LagID IN (SELECT Lag_ID FROM matchmate_se_db.MM_Lag));

DROP TABLE IF EXISTS _orphan_game_event;
CREATE TABLE _orphan_game_event LIKE matchmate_se_db.MM_Händelser;
INSERT INTO _orphan_game_event
SELECT * FROM matchmate_se_db.MM_Händelser H
WHERE NOT (
        H.MatchID    IN (SELECT MatchID FROM matchmate_se_db.MM_Matcher
                         WHERE LagID IN (SELECT Lag_ID FROM matchmate_se_db.MM_Lag))
    AND H.SpelareID  IN (SELECT SpelarID FROM matchmate_se_db.MM_Spelare
                         WHERE LagID IN (SELECT Lag_ID FROM matchmate_se_db.MM_Lag))
    AND H.HändelseID IN (SELECT HändelseID FROM matchmate_se_db.MM_HändelseLista)
    AND H.Fas        IN (SELECT FasID FROM matchmate_se_db.MM_Fas)
    AND H.Zon        IN (SELECT ZonID FROM matchmate_se_db.MM_Zon)
);

SET FOREIGN_KEY_CHECKS = 1;
