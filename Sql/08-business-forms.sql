-- ============================================================
-- Columns and tables behind the web forms: vehicle, driver, trip budget, costs,
-- compliance documents, maintenance log, trailer changes, moderation, uploaded files.
--
-- A new database gets them from the EF model (Program.cs creates the tables); this
-- script brings an existing database to the same shape. Created idempotently at
-- startup (Program.cs); safe to run again. Nothing is dropped or rewritten.
-- ============================================================

ALTER TABLE companies ADD COLUMN IF NOT EXISTS dispatch_phone text;

-- "Novi vozač"
ALTER TABLE drivers
    ADD COLUMN IF NOT EXISTS employee_number text,
    ADD COLUMN IF NOT EXISTS email text,
    ADD COLUMN IF NOT EXISTS license_categories text,
    ADD COLUMN IF NOT EXISTS license_expiry date,
    ADD COLUMN IF NOT EXISTS code95expiry date,
    ADD COLUMN IF NOT EXISTS adr_expiry date,
    ADD COLUMN IF NOT EXISTS tachograph_card_number text,
    ADD COLUMN IF NOT EXISTS tachograph_card_expiry date,
    ADD COLUMN IF NOT EXISTS medical_expiry date,
    ADD COLUMN IF NOT EXISTS hire_date date,
    ADD COLUMN IF NOT EXISTS address text,
    ADD COLUMN IF NOT EXISTS notes text,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

-- "Novo vozilo"
ALTER TABLE vehicles
    ADD COLUMN IF NOT EXISTS name text,
    ADD COLUMN IF NOT EXISTS fleet_number text,
    ADD COLUMN IF NOT EXISTS registration_date date,
    ADD COLUMN IF NOT EXISTS registration_expiry date,
    ADD COLUMN IF NOT EXISTS registration_office text,
    ADD COLUMN IF NOT EXISTS registration_document text,
    ADD COLUMN IF NOT EXISTS engine text,
    ADD COLUMN IF NOT EXISTS engine_power_kw integer,
    ADD COLUMN IF NOT EXISTS emission_class text,
    ADD COLUMN IF NOT EXISTS empty_weight_tons numeric,
    ADD COLUMN IF NOT EXISTS department text,
    ADD COLUMN IF NOT EXISTS fuel_card text,
    ADD COLUMN IF NOT EXISTS notes text,
    ADD COLUMN IF NOT EXISTS trailer_id uuid,
    ADD COLUMN IF NOT EXISTS technical_inspection_expiry date,
    ADD COLUMN IF NOT EXISTS tir_expiry date,
    ADD COLUMN IF NOT EXISTS euro_confirmation_expiry date,
    ADD COLUMN IF NOT EXISTS tachograph_calibration_expiry date,
    ADD COLUMN IF NOT EXISTS odometer_km integer,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

-- Trip number, budget and plan (Finansije, Dispečing, portal vozača)
ALTER TABLE trips
    ADD COLUMN IF NOT EXISTS trip_number text,
    ADD COLUMN IF NOT EXISTS budget_amount numeric,
    ADD COLUMN IF NOT EXISTS currency text NOT NULL DEFAULT 'EUR',
    ADD COLUMN IF NOT EXISTS planned_start_at timestamptz,
    ADD COLUMN IF NOT EXISTS planned_arrival_at timestamptz,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

-- "Novi trošak"
ALTER TABLE transport_costs
    ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS incurred_at timestamptz,
    ADD COLUMN IF NOT EXISTS location text,
    ADD COLUMN IF NOT EXISTS country_code text,
    ADD COLUMN IF NOT EXISTS payment_method text,
    ADD COLUMN IF NOT EXISTS card_label text,
    ADD COLUMN IF NOT EXISTS odometer_km integer,
    ADD COLUMN IF NOT EXISTS vehicle_id uuid,
    ADD COLUMN IF NOT EXISTS driver_id uuid,
    ADD COLUMN IF NOT EXISTS receipt_file_id uuid,
    ADD COLUMN IF NOT EXISTS created_by_user_id uuid;

CREATE TABLE IF NOT EXISTS compliance_documents (
    id                 uuid        NOT NULL,
    company_id         uuid        NOT NULL,
    vehicle_id         uuid        NOT NULL,
    document_type      text        NOT NULL,
    number             text,
    issue_date         date,
    expiry_date        date        NOT NULL,
    issuer             text,
    note               text,
    file_id            uuid,
    created_at         timestamptz NOT NULL,
    created_by_user_id uuid,
    CONSTRAINT pk_compliance_documents PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_compliance_documents_company_id ON compliance_documents (company_id);
CREATE INDEX IF NOT EXISTS ix_compliance_documents_vehicle_id ON compliance_documents (vehicle_id);

CREATE TABLE IF NOT EXISTS vehicle_log_entries (
    id                 uuid        NOT NULL,
    company_id         uuid        NOT NULL,
    vehicle_id         uuid        NOT NULL,
    kind               integer     NOT NULL,
    title              text        NOT NULL,
    description        text,
    occurred_at        timestamptz NOT NULL,
    odometer_km        integer,
    cost               numeric,
    currency           text        NOT NULL,
    status             integer     NOT NULL,
    severity           text        NOT NULL,
    workshop           text,
    resolved_at        timestamptz,
    created_by_name    text,
    created_by_user_id uuid,
    created_at         timestamptz NOT NULL,
    CONSTRAINT pk_vehicle_log_entries PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_vehicle_log_entries_company_id ON vehicle_log_entries (company_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_log_entries_vehicle_id ON vehicle_log_entries (vehicle_id);

CREATE TABLE IF NOT EXISTS trailer_assignments (
    id                  uuid        NOT NULL,
    company_id          uuid        NOT NULL,
    vehicle_id          uuid        NOT NULL,
    trailer_id          uuid,
    assigned_at         timestamptz NOT NULL,
    assigned_by_user_id uuid,
    CONSTRAINT pk_trailer_assignments PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_trailer_assignments_company_id ON trailer_assignments (company_id);
CREATE INDEX IF NOT EXISTS ix_trailer_assignments_vehicle_id ON trailer_assignments (vehicle_id);

CREATE TABLE IF NOT EXISTS moderation_requests (
    id                   uuid        NOT NULL,
    company_id           uuid        NOT NULL,
    number               text        NOT NULL,
    title                text        NOT NULL,
    description          text,
    domain               text        NOT NULL,
    risk                 text        NOT NULL,
    status               text        NOT NULL,
    requested_by_name    text,
    requested_by_user_id uuid,
    vehicle_id           uuid,
    trip_id              uuid,
    impact_summary       text,
    created_at           timestamptz NOT NULL,
    decided_at           timestamptz,
    decided_by_user_id   uuid,
    decided_by_name      text,
    decision_note        text,
    CONSTRAINT pk_moderation_requests PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_moderation_requests_company_id ON moderation_requests (company_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_moderation_requests_company_id_number ON moderation_requests (company_id, number);

CREATE TABLE IF NOT EXISTS stored_files (
    id                 uuid        NOT NULL,
    company_id         uuid        NOT NULL,
    file_name          text        NOT NULL,
    content_type       text        NOT NULL,
    size_bytes         bigint      NOT NULL,
    storage_path       text        NOT NULL,
    created_at         timestamptz NOT NULL,
    created_by_user_id uuid,
    CONSTRAINT pk_stored_files PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_stored_files_company_id ON stored_files (company_id);
