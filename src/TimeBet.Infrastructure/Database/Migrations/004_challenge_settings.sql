CREATE TABLE challenge_settings (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    updated_at_utc_ms INTEGER NOT NULL
);

INSERT INTO challenge_settings (singleton_id, enabled, updated_at_utc_ms)
VALUES (1, 0, unixepoch('subsec') * 1000);

CREATE INDEX IF NOT EXISTS ix_game_rounds_played_at
    ON game_rounds (played_at_utc_ms DESC);
