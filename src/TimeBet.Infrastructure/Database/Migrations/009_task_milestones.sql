CREATE TABLE IF NOT EXISTS planning_task_milestones (
    id TEXT NOT NULL PRIMARY KEY,
    task_id TEXT NOT NULL REFERENCES planning_tasks (id) ON DELETE CASCADE,
    content TEXT NOT NULL CHECK (length(trim(content)) > 0),
    sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
    is_completed INTEGER NOT NULL DEFAULT 0 CHECK (is_completed IN (0, 1)),
    created_at_utc_ms INTEGER NOT NULL,
    completed_at_utc_ms INTEGER NULL,
    CHECK (is_completed = 1 OR completed_at_utc_ms IS NULL),
    CHECK (is_completed = 0 OR completed_at_utc_ms IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_planning_task_milestones_task
    ON planning_task_milestones (task_id, sort_order, created_at_utc_ms, id);
