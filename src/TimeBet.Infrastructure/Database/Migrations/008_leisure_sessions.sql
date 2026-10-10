CREATE TABLE IF NOT EXISTS leisure_sessions (
    id TEXT NOT NULL PRIMARY KEY,
    activity TEXT NOT NULL CHECK (length(trim(activity)) > 0),
    planned_seconds INTEGER NOT NULL CHECK (planned_seconds > 0),
    status TEXT NOT NULL CHECK (status IN ('running', 'paused', 'completed')),
    started_at_utc_ms INTEGER NOT NULL,
    last_transition_at_utc_ms INTEGER NOT NULL,
    paused_at_utc_ms INTEGER NULL,
    ended_at_utc_ms INTEGER NULL,
    accumulated_paused_seconds INTEGER NOT NULL DEFAULT 0 CHECK (accumulated_paused_seconds >= 0),
    effective_duration_seconds INTEGER NULL CHECK (effective_duration_seconds IS NULL OR effective_duration_seconds > 0),
    charged_seconds INTEGER NOT NULL DEFAULT 0 CHECK (charged_seconds >= 0),
    completion_note TEXT NULL,
    active_slot INTEGER NULL CHECK (active_slot IS NULL OR active_slot = 1),
    CHECK (last_transition_at_utc_ms >= started_at_utc_ms),
    CHECK (charged_seconds <= COALESCE(effective_duration_seconds, 0)),
    CHECK (status <> 'completed' OR (ended_at_utc_ms IS NOT NULL AND effective_duration_seconds IS NOT NULL AND active_slot IS NULL)),
    CHECK (status = 'completed' OR (ended_at_utc_ms IS NULL AND effective_duration_seconds IS NULL AND charged_seconds = 0 AND active_slot = 1))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_leisure_sessions_active_slot
    ON leisure_sessions (active_slot)
    WHERE active_slot IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_leisure_sessions_started_at
    ON leisure_sessions (started_at_utc_ms);
