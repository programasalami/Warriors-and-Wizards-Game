-- Hybrid relational/jsonb schema. Only columns that are actually queried or
-- indexed by application code are promoted to real columns; everything else
-- (nested stat blocks, embedded Characters/VaultChests lists, etc.) rides
-- along as one jsonb blob per row, matching the embedded-document shape the
-- LiteDB models already have. This keeps DbClient's method bodies close to
-- their original form - every write is still a whole-object replace, exactly
-- like the old LiteDB Upsert calls were.

CREATE TABLE IF NOT EXISTS accounts (
    id       SERIAL PRIMARY KEY,
    name     TEXT NOT NULL UNIQUE,
    guild_id INT  NOT NULL DEFAULT 0,
    data     JSONB NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_accounts_guild_id ON accounts (guild_id);

CREATE TABLE IF NOT EXISTS logins (
    id            SERIAL PRIMARY KEY,
    name          TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    ip_address    TEXT NOT NULL,
    last_login_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_logins_ip_address ON logins (ip_address);

CREATE TABLE IF NOT EXISTS guilds (
    id           SERIAL PRIMARY KEY,
    name         TEXT NOT NULL UNIQUE,
    level        SMALLINT NOT NULL DEFAULT 0,
    current_fame BIGINT NOT NULL DEFAULT 0,
    total_fame   BIGINT NOT NULL DEFAULT 0,
    guild_board  TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS bans (
    id               SERIAL PRIMARY KEY,
    target_acc_id    INT NOT NULL,
    moderator_acc_id INT NOT NULL,
    reason           TEXT,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at       TIMESTAMPTZ,
    permanent        BOOLEAN NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS idx_bans_target_acc_id ON bans (target_acc_id);

CREATE TABLE IF NOT EXISTS mutes (
    id               SERIAL PRIMARY KEY,
    target_acc_id    INT NOT NULL,
    moderator_acc_id INT NOT NULL,
    reason           TEXT,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at       TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_mutes_target_acc_id ON mutes (target_acc_id);

-- The Bug Board in the Nexus: anyone signed in can post, everyone can read, admins check / mark / delete (see BugBoardRules).
CREATE TABLE IF NOT EXISTS bug_posts (
    id          SERIAL PRIMARY KEY,
    account_id  INT NOT NULL,
    author      TEXT NOT NULL,
    message     TEXT NOT NULL,
    status      TEXT NOT NULL DEFAULT 'new',
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_bug_posts_created_at ON bug_posts (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_bug_posts_account_id ON bug_posts (account_id, created_at DESC);

-- The Inbox (Character Book): messages to a player, optionally with gold / fame to claim (see RewardsDb).
CREATE TABLE IF NOT EXISTS inbox_messages (
    id          SERIAL PRIMARY KEY,
    acc_id      INT NOT NULL,
    sender      TEXT NOT NULL,
    subject     TEXT NOT NULL,
    body        TEXT NOT NULL,
    gold        INT NOT NULL DEFAULT 0,
    fame        INT NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_read     BOOLEAN NOT NULL DEFAULT false,
    claimed     BOOLEAN NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS idx_inbox_acc_id ON inbox_messages (acc_id, created_at DESC);

-- Daily Gift streak and Daily Spin (each usable once every 24 hours), see DailyCooldown / DailyGiftRules / SpinWheel.
-- last_gift_at / last_spin_at are the exact moments of the last claim. last_gift_day / last_spin_day are the OLD "which UTC day" columns: still read when the
-- new column is empty (a claim made before the change), never written any more.
CREATE TABLE IF NOT EXISTS daily_rewards (
    acc_id         INT PRIMARY KEY,
    last_gift_day  DATE,
    gift_streak    INT NOT NULL DEFAULT 0,
    last_spin_day  DATE
);
ALTER TABLE daily_rewards ADD COLUMN IF NOT EXISTS last_gift_at TIMESTAMPTZ;
ALTER TABLE daily_rewards ADD COLUMN IF NOT EXISTS last_spin_at TIMESTAMPTZ;
-- A claim made today (before the exact time was recorded) counts from the moment this runs, so its timer starts at a full 24 hours instead of a guess. Runs at every
-- start but only touches rows that still have no exact time; older claims fall back to noon UTC of their day (RewardsDb.ReadDailyAsync).
UPDATE daily_rewards SET last_gift_at = now() WHERE last_gift_at IS NULL AND last_gift_day = (now() AT TIME ZONE 'UTC')::date;
UPDATE daily_rewards SET last_spin_at = now() WHERE last_spin_at IS NULL AND last_spin_day = (now() AT TIME ZONE 'UTC')::date;
