-- =====================================================================
--  Härdning för offline-synk: gör inserts idempotenta.
--
--  Offline-klienten sätter ett unikt clientId per registrerad händelse.
--  Genom att lagra det i game_event.client_event_id med UNIQUE-index blir
--  en omsynkad händelse (t.ex. om nätet dog mellan insert och svar) en
--  no-op i stället för en dubblett.
--
--  Kolumnen är NULL för alla online-inserts (form-POST) och äldre rader –
--  MySQL tillåter flera NULL i ett UNIQUE-index, så inget påverkas där.
--
--  DB: matchmate_se_db_V2 @ mysql69.unoeuro.com
-- =====================================================================

ALTER TABLE game_event
    ADD COLUMN client_event_id VARCHAR(64) NULL AFTER id,
    ADD UNIQUE KEY uq_game_event_client_event_id (client_event_id);
