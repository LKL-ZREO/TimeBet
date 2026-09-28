CREATE TABLE IF NOT EXISTS ai_settings (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    base_url TEXT NOT NULL,
    model TEXT NOT NULL,
    api_key_protected BLOB NULL,
    updated_at_utc_ms INTEGER NOT NULL
);

INSERT INTO ai_settings
    (singleton_id, enabled, base_url, model, api_key_protected, updated_at_utc_ms)
VALUES
    (1, 0, 'https://api.openai.com/v1', 'gpt-4o-mini', NULL, unixepoch('subsec') * 1000);
