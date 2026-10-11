-- ============================================================
-- Truck driving bans (weekend, holiday and special bans per country and day).
--
-- Filled by TruckBanSyncService from the truckban.eu calendar. Nothing reaches the map,
-- the router or the phones before an administrator approves it on /truck-bans: a new or
-- changed ban arrives as 'pending', a ban that disappears at the source is only flagged
-- (removed_at_source) until the removal is confirmed.
--
-- Created idempotently at startup (Program.cs); safe to run again.
-- ============================================================

CREATE TABLE IF NOT EXISTS truck_bans (
    id                bigserial PRIMARY KEY,
    country_code      text        NOT NULL,              -- ISO 3166-1 alpha-2
    country_name      text        NOT NULL,              -- as named by the source
    ban_date          date        NOT NULL,              -- local date in the country
    start_minute      integer     NOT NULL CHECK (start_minute BETWEEN 0 AND 1440),
    end_minute        integer     NOT NULL CHECK (end_minute BETWEEN 0 AND 1440),  -- 1440 = 24:00
    regional          boolean     NOT NULL,              -- only some roads, see info
    info              text        NOT NULL DEFAULT '',
    info_hash         text        NOT NULL,              -- md5 of info, part of the identity
    source            text        NOT NULL DEFAULT 'truckban.eu',
    status            text        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'rejected')),
    first_seen_at     timestamptz NOT NULL DEFAULT now(),
    last_seen_at      timestamptz NOT NULL DEFAULT now(),
    removed_at_source timestamptz,
    reviewed_by       text,
    reviewed_at       timestamptz,
    CONSTRAINT ux_truck_bans_identity UNIQUE (country_code, ban_date, start_minute, end_minute, regional, info_hash)
);

CREATE INDEX IF NOT EXISTS ix_truck_bans_date_status ON truck_bans (ban_date, status);

-- Per country: which trucks a ban applies to. min_weight_t stays NULL until an
-- administrator sets it from the source text (vehicles_text); NULL means every truck.
CREATE TABLE IF NOT EXISTS truck_ban_countries (
    country_code       text PRIMARY KEY,
    country_name       text        NOT NULL,
    time_zone          text        NOT NULL,             -- IANA, for the local ban times
    min_weight_t       numeric(5,2),
    vehicles_text      text,                             -- "Vehicles" section of the source page
    vehicles_text_hash text,
    needs_review       boolean     NOT NULL DEFAULT true, -- source text changed since the last review
    updated_at         timestamptz NOT NULL DEFAULT now()
);
