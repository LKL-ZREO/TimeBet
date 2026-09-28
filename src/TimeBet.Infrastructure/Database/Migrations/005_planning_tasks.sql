CREATE TABLE IF NOT EXISTS planning_tasks (
    id TEXT NOT NULL PRIMARY KEY,
    title TEXT NOT NULL CHECK (length(trim(title)) > 0),
    estimated_seconds INTEGER NULL CHECK (estimated_seconds IS NULL OR estimated_seconds > 0),
    actual_seconds INTEGER NOT NULL DEFAULT 0 CHECK (actual_seconds >= 0),
    status TEXT NOT NULL CHECK (status IN ('active', 'completed', 'archived')),
    created_at_utc_ms INTEGER NOT NULL,
    completed_at_utc_ms INTEGER NULL,
    CHECK (status <> 'completed' OR completed_at_utc_ms IS NOT NULL),
    CHECK (status = 'completed' OR completed_at_utc_ms IS NULL)
);

CREATE INDEX IF NOT EXISTS ix_planning_tasks_status_created
    ON planning_tasks (status, created_at_utc_ms, id);

ALTER TABLE study_sessions
ADD COLUMN task_id TEXT NULL REFERENCES planning_tasks (id);

CREATE INDEX IF NOT EXISTS ix_study_sessions_task_id
    ON study_sessions (task_id);
