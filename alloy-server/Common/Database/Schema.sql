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
