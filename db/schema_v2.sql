-- =====================================================================
--  MatchMate – ny databasdesign (v2)
--  Målsdatabas: matchmate_se_db_V2  (mysql69.unoeuro.com)
--
--  Full omdesign: engelska snake_case-namn, utf8mb4, InnoDB,
--  primärnycklar, foreign keys och index.
--  Den gamla databasen matchmate_se_db lämnas helt orörd.
-- =====================================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ---------- Lookup-tabeller ----------

DROP TABLE IF EXISTS event_type;
CREATE TABLE event_type (           -- var MM_HändelseLista (+ MM_HändelseKategorier)
    id       INT UNSIGNED NOT NULL PRIMARY KEY,        -- behåller HändelseID
    text     VARCHAR(50)  NOT NULL,                    -- exakt bevarad (statistik bygger på LIKE mot denna)
    is_goal  BOOLEAN      NOT NULL DEFAULT 0            -- ersätter separata kategoritabellen ('Mål')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS phase;
CREATE TABLE phase (                -- var MM_Fas
    id    TINYINT UNSIGNED NOT NULL PRIMARY KEY,        -- behåller FasID (0..3)
    name  VARCHAR(15)      NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS zone;
CREATE TABLE zone (                 -- var MM_Zon
    id    TINYINT UNSIGNED NOT NULL PRIMARY KEY,        -- behåller ZonID (0..5)
    name  VARCHAR(15)      NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

-- ---------- Kärntabeller ----------

DROP TABLE IF EXISTS app_user;
CREATE TABLE app_user (             -- var MM_Users
    id             INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,  -- behåller UserID
    email          VARCHAR(255) NOT NULL,                            -- var UserName (är en e-post)
    password_hash  VARCHAR(255) NULL,                                -- var Password
    role           TINYINT UNSIGNED NOT NULL,                        -- var typ (1/2)
    status         VARCHAR(10)  NOT NULL DEFAULT 'aktiv',
    created_at     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_app_user_email (email)                             -- Password2 slopad (redundant)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS user_delegate;
CREATE TABLE user_delegate (        -- var MM_UserUser (användare som agerar för annan)
    user_id           INT UNSIGNED NOT NULL,           -- var userID
    delegate_user_id  INT UNSIGNED NOT NULL,           -- var useruserID
    PRIMARY KEY (user_id, delegate_user_id),
    CONSTRAINT fk_deleg_user     FOREIGN KEY (user_id)          REFERENCES app_user(id) ON DELETE CASCADE,
    CONSTRAINT fk_deleg_delegate FOREIGN KEY (delegate_user_id) REFERENCES app_user(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS team;
CREATE TABLE team (                 -- var MM_Lag
    id          INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,  -- behåller Lag_ID
    name        VARCHAR(50)  NOT NULL,                             -- Namn
    series      VARCHAR(50)  NULL,                                 -- Serie
    home_venue  VARCHAR(100) NOT NULL,                             -- HemmaHall
    category    CHAR(1)      NOT NULL,                             -- KM (D/H/M/W)
    user_id     INT UNSIGNED NOT NULL,                             -- userID
    CONSTRAINT fk_team_user FOREIGN KEY (user_id) REFERENCES app_user(id),
    KEY ix_team_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS player;
CREATE TABLE player (               -- var MM_Spelare (+ MM_XtraNummer infälld)
    id               INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,  -- behåller SpelarID
    team_id          INT UNSIGNED NOT NULL,                             -- LagID
    first_name       VARCHAR(50)  NOT NULL,                             -- Förnamn
    last_name        VARCHAR(50)  NOT NULL,                             -- Efternamn
    position         VARCHAR(3)   NOT NULL,                             -- Position (MV, H6, ...)
    shirt_number     INT          NOT NULL,                             -- Nummer
    alt_shirt_number INT          NULL,                                 -- var MM_XtraNummer.nummer
    CONSTRAINT fk_player_team FOREIGN KEY (team_id) REFERENCES team(id) ON DELETE CASCADE,
    KEY ix_player_team (team_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS game;
CREATE TABLE game (                 -- var MM_Matcher ('match' är reserverat i MySQL)
    id         INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,  -- behåller MatchID
    team_id    INT UNSIGNED NOT NULL,                             -- LagID
    played_on  DATE         NOT NULL,                             -- Datum
    opponent   VARCHAR(50)  NOT NULL,                             -- Motståndare
    venue      VARCHAR(50)  NOT NULL,                             -- Plats
    status     VARCHAR(10)  NOT NULL DEFAULT 'Planerad',          -- Planerad/Pågående/Avslutad (värden bevaras)
    CONSTRAINT fk_game_team FOREIGN KEY (team_id) REFERENCES team(id) ON DELETE CASCADE,
    KEY ix_game_team (team_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

DROP TABLE IF EXISTS game_event;
CREATE TABLE game_event (           -- var MM_Händelser (fick äntligen en primärnyckel)
    id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    game_id        INT UNSIGNED NOT NULL,                          -- MatchID
    player_id      INT UNSIGNED NOT NULL,                          -- SpelareID
    event_type_id  INT UNSIGNED NOT NULL,                          -- HändelseID
    phase_id       TINYINT UNSIGNED NOT NULL DEFAULT 0,            -- Fas
    zone_id        TINYINT UNSIGNED NOT NULL DEFAULT 0,            -- Zon
    seconds        INT NOT NULL,                                   -- Tids (COALESCE med Tid), redundant TIME slopad
    CONSTRAINT fk_evt_game   FOREIGN KEY (game_id)       REFERENCES game(id)       ON DELETE CASCADE,
    CONSTRAINT fk_evt_player FOREIGN KEY (player_id)     REFERENCES player(id)     ON DELETE CASCADE,
    CONSTRAINT fk_evt_type   FOREIGN KEY (event_type_id) REFERENCES event_type(id),
    CONSTRAINT fk_evt_phase  FOREIGN KEY (phase_id)      REFERENCES phase(id),
    CONSTRAINT fk_evt_zone   FOREIGN KEY (zone_id)       REFERENCES zone(id),
    KEY ix_evt_game (game_id),
    KEY ix_evt_player (player_id),
    KEY ix_evt_type (event_type_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_swedish_ci;

SET FOREIGN_KEY_CHECKS = 1;
