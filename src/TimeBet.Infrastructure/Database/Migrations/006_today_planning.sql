ALTER TABLE planning_tasks
ADD COLUMN due_date TEXT NULL;

CREATE TABLE IF NOT EXISTS planning_time_blocks (
    id TEXT NOT NULL PRIMARY KEY,
    task_id TEXT NOT NULL REFERENCES planning_tasks (id),
    starts_at_utc_ms INTEGER NOT NULL,
    planned_seconds INTEGER NOT NULL CHECK (planned_seconds > 0),
    actual_seconds INTEGER NOT NULL DEFAULT 0 CHECK (actual_seconds >= 0),
    status TEXT NOT NULL CHECK (status IN ('planned', 'completed', 'skipped')),
    created_at_utc_ms INTEGER NOT NULL,
    resolved_at_utc_ms INTEGER NULL,
    CHECK (status = 'planned' OR resolved_at_utc_ms IS NOT NULL),
    CHECK (status <> 'planned' OR resolved_at_utc_ms IS NULL),
    CHECK (status <> 'skipped' OR actual_seconds = 0)
);

CREATE INDEX IF NOT EXISTS ix_planning_time_blocks_start
    ON planning_time_blocks (starts_at_utc_ms, id);

CREATE INDEX IF NOT EXISTS ix_planning_time_blocks_task
    ON planning_time_blocks (task_id, starts_at_utc_ms);

ALTER TABLE study_sessions
ADD COLUMN time_block_id TEXT NULL REFERENCES planning_time_blocks (id);

CREATE INDEX IF NOT EXISTS ix_study_sessions_time_block_id
    ON study_sessions (time_block_id);
