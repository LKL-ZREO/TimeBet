ALTER TABLE activity_segments
    ADD COLUMN is_idle INTEGER NOT NULL DEFAULT 0 CHECK (is_idle IN (0, 1));

CREATE TABLE tracking_settings (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    capture_activity INTEGER NOT NULL CHECK (capture_activity IN (0, 1)),
    capture_screenshots INTEGER NOT NULL CHECK (capture_screenshots IN (0, 1)),
    screenshot_interval_minutes INTEGER NOT NULL CHECK (screenshot_interval_minutes IN (1, 5, 10, 15, 30)),
    screenshot_retention_days INTEGER NOT NULL CHECK (screenshot_retention_days IN (7, 30, 90, 3650)),
    idle_threshold_minutes INTEGER NOT NULL CHECK (idle_threshold_minutes BETWEEN 1 AND 60),
    updated_at_utc_ms INTEGER NOT NULL,
    CHECK (capture_screenshots = 0 OR capture_activity = 1)
);

INSERT INTO tracking_settings
    (singleton_id, capture_activity, capture_screenshots, screenshot_interval_minutes,
     screenshot_retention_days, idle_threshold_minutes, updated_at_utc_ms)
VALUES (1, 1, 0, 5, 7, 5, unixepoch('subsec') * 1000);

CREATE INDEX IF NOT EXISTS ix_screenshot_records_captured_at
    ON screenshot_records (captured_at_utc_ms);
