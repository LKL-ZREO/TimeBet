CREATE TABLE IF NOT EXISTS study_sessions (
    id TEXT NOT NULL PRIMARY KEY,
    goal TEXT NOT NULL CHECK (length(trim(goal)) > 0),
    status TEXT NOT NULL CHECK (status IN ('running', 'paused', 'completed')),
    started_at_utc_ms INTEGER NOT NULL,
    last_transition_at_utc_ms INTEGER NOT NULL,
    paused_at_utc_ms INTEGER NULL,
    ended_at_utc_ms INTEGER NULL,
    accumulated_paused_seconds INTEGER NOT NULL DEFAULT 0 CHECK (accumulated_paused_seconds >= 0),
    effective_duration_seconds INTEGER NULL CHECK (effective_duration_seconds IS NULL OR effective_duration_seconds > 0),
    completion_note TEXT NULL,
    next_event_sequence INTEGER NOT NULL CHECK (next_event_sequence >= 2),
    active_slot INTEGER NULL CHECK (active_slot IS NULL OR active_slot = 1),
    CHECK (last_transition_at_utc_ms >= started_at_utc_ms),
    CHECK (status <> 'completed' OR (ended_at_utc_ms IS NOT NULL AND effective_duration_seconds IS NOT NULL AND active_slot IS NULL)),
    CHECK (status = 'completed' OR (ended_at_utc_ms IS NULL AND effective_duration_seconds IS NULL AND active_slot = 1))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_study_sessions_active_slot
    ON study_sessions (active_slot)
    WHERE active_slot IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_study_sessions_started_at
    ON study_sessions (started_at_utc_ms);

CREATE TABLE IF NOT EXISTS session_events (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES study_sessions (id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL CHECK (sequence >= 1),
    event_type TEXT NOT NULL CHECK (event_type IN ('started', 'paused', 'resumed', 'completed')),
    occurred_at_utc_ms INTEGER NOT NULL,
    UNIQUE (session_id, sequence)
);

CREATE INDEX IF NOT EXISTS ix_session_events_session_time
    ON session_events (session_id, occurred_at_utc_ms);

CREATE TABLE IF NOT EXISTS session_tasks (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES study_sessions (id) ON DELETE CASCADE,
    content TEXT NOT NULL CHECK (length(trim(content)) > 0),
    is_completed INTEGER NOT NULL CHECK (is_completed IN (0, 1)),
    sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
    UNIQUE (session_id, sort_order)
);

CREATE TABLE IF NOT EXISTS ledger_entries (
    id TEXT NOT NULL PRIMARY KEY,
    entry_type TEXT NOT NULL CHECK (entry_type IN ('earn', 'spend', 'game-win', 'game-loss', 'adjustment')),
    amount_seconds INTEGER NOT NULL CHECK (amount_seconds <> 0),
    source_type TEXT NOT NULL,
    source_id TEXT NOT NULL,
    note TEXT NULL,
    occurred_at_utc_ms INTEGER NOT NULL,
    UNIQUE (source_type, source_id),
    CHECK (
        (entry_type IN ('earn', 'game-win') AND amount_seconds > 0)
        OR (entry_type IN ('spend', 'game-loss') AND amount_seconds < 0)
        OR entry_type = 'adjustment'
    )
);

CREATE INDEX IF NOT EXISTS ix_ledger_entries_occurred_at
    ON ledger_entries (occurred_at_utc_ms);

CREATE TRIGGER IF NOT EXISTS trg_ledger_entries_no_update
BEFORE UPDATE ON ledger_entries
BEGIN
    SELECT RAISE(ABORT, 'ledger entries are immutable');
END;

CREATE TRIGGER IF NOT EXISTS trg_ledger_entries_no_delete
BEFORE DELETE ON ledger_entries
BEGIN
    SELECT RAISE(ABORT, 'ledger entries are immutable');
END;

CREATE TABLE IF NOT EXISTS rewards (
    id TEXT NOT NULL PRIMARY KEY,
    name TEXT NOT NULL CHECK (length(trim(name)) > 0),
    description TEXT NULL,
    cost_seconds INTEGER NOT NULL CHECK (cost_seconds > 0),
    image_path TEXT NULL,
    is_active INTEGER NOT NULL CHECK (is_active IN (0, 1)),
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS reward_redemptions (
    id TEXT NOT NULL PRIMARY KEY,
    reward_id TEXT NOT NULL REFERENCES rewards (id),
    ledger_entry_id TEXT NOT NULL UNIQUE REFERENCES ledger_entries (id),
    reward_name_snapshot TEXT NOT NULL,
    cost_seconds INTEGER NOT NULL CHECK (cost_seconds > 0),
    redeemed_at_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS activity_segments (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES study_sessions (id) ON DELETE CASCADE,
    started_at_utc_ms INTEGER NOT NULL,
    ended_at_utc_ms INTEGER NOT NULL CHECK (ended_at_utc_ms > started_at_utc_ms),
    process_name TEXT NOT NULL,
    window_title TEXT NOT NULL,
    document_name TEXT NULL,
    document_type TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_activity_segments_session_time
    ON activity_segments (session_id, started_at_utc_ms);

CREATE TABLE IF NOT EXISTS screenshot_records (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES study_sessions (id) ON DELETE CASCADE,
    activity_segment_id TEXT NULL REFERENCES activity_segments (id) ON DELETE SET NULL,
    captured_at_utc_ms INTEGER NOT NULL,
    file_path TEXT NOT NULL UNIQUE,
    thumbnail_path TEXT NULL,
    width INTEGER NOT NULL CHECK (width > 0),
    height INTEGER NOT NULL CHECK (height > 0),
    file_size_bytes INTEGER NOT NULL CHECK (file_size_bytes >= 0)
);

CREATE INDEX IF NOT EXISTS ix_screenshot_records_session_time
    ON screenshot_records (session_id, captured_at_utc_ms);

CREATE TABLE IF NOT EXISTS game_rounds (
    id TEXT NOT NULL PRIMARY KEY,
    wager_seconds INTEGER NOT NULL CHECK (wager_seconds > 0),
    payout_seconds INTEGER NOT NULL CHECK (payout_seconds >= 0),
    probability_version TEXT NOT NULL,
    outcome REAL NOT NULL CHECK (outcome >= 0 AND outcome < 1),
    ledger_entry_id TEXT NOT NULL UNIQUE REFERENCES ledger_entries (id),
    played_at_utc_ms INTEGER NOT NULL
);
