-- ============================================================
-- Transport order finance: price, driver's share (percent) and the cash advance
-- given to the driver for business expenses.
--
-- A new database gets the columns from the EF model; this script brings an existing
-- database to the same shape. Run idempotently at startup (Program.cs); safe to run
-- again. Nothing is dropped or rewritten.
-- ============================================================

ALTER TABLE transport_orders
    ADD COLUMN IF NOT EXISTS transport_price numeric,
    ADD COLUMN IF NOT EXISTS driver_earning_percent numeric,
    ADD COLUMN IF NOT EXISTS advance_amount numeric,
    ADD COLUMN IF NOT EXISTS currency text NOT NULL DEFAULT 'EUR';
