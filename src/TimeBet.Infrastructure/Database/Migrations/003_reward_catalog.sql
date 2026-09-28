CREATE INDEX IF NOT EXISTS ix_rewards_active_updated
    ON rewards (is_active, updated_at_utc_ms DESC);

CREATE INDEX IF NOT EXISTS ix_reward_redemptions_redeemed_at
    ON reward_redemptions (redeemed_at_utc_ms DESC);
