-- ============================================================
-- Chat between drivers (mobile app, Poruke) and dispatch (web, Poruke).
--
-- A new database gets the table from the EF model (Program.cs creates the tables); this
-- script brings an existing database to the same shape. Run idempotently at startup
-- (Program.cs); safe to run again. Nothing is dropped or rewritten.
-- ============================================================

CREATE TABLE IF NOT EXISTS driver_chat_messages (
    id uuid NOT NULL PRIMARY KEY,
    company_id uuid NOT NULL,
    driver_id uuid NOT NULL,
    sender_user_id uuid NULL,
    sender_name text NULL,
    from_driver boolean NOT NULL,
    text text NOT NULL,
    trip_id uuid NULL,
    created_at timestamptz NOT NULL,
    read_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_driver_chat_messages_company_id ON driver_chat_messages (company_id);
CREATE INDEX IF NOT EXISTS ix_driver_chat_messages_driver_created ON driver_chat_messages (driver_id, created_at DESC);
