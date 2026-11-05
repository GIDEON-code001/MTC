CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS tenants (
    id uuid PRIMARY KEY,
    name varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS fleet_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    email varchar(254) NULL,
    phone_number varchar(24) NULL,
    password_hash text NOT NULL,
    role varchar(32) NOT NULL DEFAULT 'OperationsManager',
    rider_id uuid NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_fleet_users_tenant_email UNIQUE (tenant_id, email),
    CONSTRAINT uq_fleet_users_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_fleet_users_tenant_phone UNIQUE (tenant_id, phone_number)
);

CREATE TABLE IF NOT EXISTS motorcycles (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    registration_number varchar(20) NOT NULL,
    make varchar(80) NOT NULL,
    model varchar(80) NOT NULL,
    engine_number varchar(80) NULL,
    chassis_number varchar(80) NULL,
    traccar_device_id bigint NULL,
    year integer NOT NULL,
    mileage_km double precision NOT NULL DEFAULT 0,
    status varchar(24) NOT NULL DEFAULT 'Available',
    assigned_driver_id uuid NULL,
    latitude double precision NULL,
    longitude double precision NULL,
    location_recorded_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_motorcycles_tenant_registration UNIQUE (tenant_id, registration_number),
    CONSTRAINT uq_motorcycles_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT ck_motorcycles_year CHECK (year BETWEEN 1990 AND 2100),
    CONSTRAINT ck_motorcycles_mileage CHECK (mileage_km >= 0),
    CONSTRAINT ck_motorcycles_location CHECK (
        (latitude IS NULL AND longitude IS NULL) OR
        (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)
    )
);

CREATE TABLE IF NOT EXISTS riders (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    full_name varchar(160) NOT NULL,
    phone_number varchar(24) NOT NULL,
    national_id varchar(40) NULL,
    license_number varchar(60) NOT NULL,
    status varchar(24) NOT NULL DEFAULT 'Active',
    daily_remittance_ugx bigint NOT NULL DEFAULT 0 CHECK (daily_remittance_ugx >= 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_drivers_tenant_phone UNIQUE (tenant_id, phone_number),
    CONSTRAINT uq_drivers_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_drivers_tenant_license UNIQUE (tenant_id, license_number)
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_fleet_users_rider'
          AND conrelid = 'fleet_users'::regclass
    ) THEN
        ALTER TABLE fleet_users
            ADD CONSTRAINT fk_fleet_users_rider
            FOREIGN KEY (tenant_id, rider_id) REFERENCES riders(tenant_id, id);
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_motorcycles_assigned_driver'
          AND conrelid = 'motorcycles'::regclass
    ) THEN
        ALTER TABLE motorcycles
            ADD CONSTRAINT fk_motorcycles_assigned_driver
            FOREIGN KEY (tenant_id, assigned_driver_id) REFERENCES riders(tenant_id, id);
    END IF;
END
$$;

CREATE TABLE IF NOT EXISTS trips (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    motorcycle_id uuid NOT NULL REFERENCES motorcycles(id),
    driver_id uuid NOT NULL,
    started_at timestamptz NOT NULL,
    ended_at timestamptz NULL,
    start_latitude double precision NULL,
    start_longitude double precision NULL,
    end_latitude double precision NULL,
    end_longitude double precision NULL,
    fare_ugx bigint NOT NULL DEFAULT 0,
    status varchar(24) NOT NULL DEFAULT 'InProgress',
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_trips_fare CHECK (fare_ugx >= 0),
    CONSTRAINT ck_trips_time CHECK (ended_at IS NULL OR ended_at >= started_at),
    CONSTRAINT fk_trips_motorcycle_tenant FOREIGN KEY (tenant_id, motorcycle_id) REFERENCES motorcycles(tenant_id, id),
    CONSTRAINT fk_trips_driver_tenant FOREIGN KEY (tenant_id, driver_id) REFERENCES riders(tenant_id, id)
);

CREATE TABLE IF NOT EXISTS payments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    driver_id uuid NOT NULL,
    motorcycle_id uuid NULL REFERENCES motorcycles(id),
    amount_ugx bigint NOT NULL,
    method varchar(24) NOT NULL,
    reference varchar(120) NULL,
    provider varchar(24) NULL,
    idempotency_key varchar(120) NULL,
    status varchar(24) NOT NULL DEFAULT 'Recorded',
    received_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_payments_amount CHECK (amount_ugx > 0),
    CONSTRAINT uq_payments_tenant_reference UNIQUE (tenant_id, reference),
    CONSTRAINT fk_payments_driver_tenant FOREIGN KEY (tenant_id, driver_id) REFERENCES riders(tenant_id, id),
    CONSTRAINT fk_payments_motorcycle_tenant FOREIGN KEY (tenant_id, motorcycle_id) REFERENCES motorcycles(tenant_id, id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_motorcycles_tenant_traccar_device ON motorcycles (tenant_id, traccar_device_id) WHERE traccar_device_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_tenant_idempotency ON payments (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_motorcycles_tenant_status ON motorcycles (tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_riders_tenant_status ON riders (tenant_id, status);
CREATE INDEX IF NOT EXISTS ix_trips_tenant_started_at ON trips (tenant_id, started_at DESC);
CREATE INDEX IF NOT EXISTS ix_trips_tenant_status ON trips (tenant_id, status);
CREATE UNIQUE INDEX IF NOT EXISTS ux_trips_active_motorcycle ON trips (tenant_id, motorcycle_id) WHERE status = 'InProgress';
CREATE UNIQUE INDEX IF NOT EXISTS ux_trips_active_driver ON trips (tenant_id, driver_id) WHERE status = 'InProgress';
CREATE INDEX IF NOT EXISTS ix_payments_tenant_received_at ON payments (tenant_id, received_at DESC);
CREATE INDEX IF NOT EXISTS ix_motorcycles_tenant_location_time ON motorcycles (tenant_id, location_recorded_at DESC);

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    user_id uuid NOT NULL,
    token_hash varchar(128) NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_refresh_tokens_tenant_hash UNIQUE (tenant_id, token_hash),
    CONSTRAINT fk_refresh_tokens_user FOREIGN KEY (tenant_id, user_id) REFERENCES fleet_users(tenant_id, id)
);

CREATE TABLE IF NOT EXISTS bike_assignments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    motorcycle_id uuid NOT NULL,
    rider_id uuid NOT NULL,
    assigned_at timestamptz NOT NULL DEFAULT now(),
    returned_at timestamptz NULL,
    assigned_by_user_id uuid NOT NULL,
    CONSTRAINT fk_assignments_motorcycle FOREIGN KEY (tenant_id, motorcycle_id) REFERENCES motorcycles(tenant_id, id),
    CONSTRAINT fk_assignments_rider FOREIGN KEY (tenant_id, rider_id) REFERENCES riders(tenant_id, id),
    CONSTRAINT fk_assignments_user FOREIGN KEY (tenant_id, assigned_by_user_id) REFERENCES fleet_users(tenant_id, id),
    CONSTRAINT ck_assignments_return CHECK (returned_at IS NULL OR returned_at >= assigned_at)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_assignments_current_bike ON bike_assignments (tenant_id, motorcycle_id) WHERE returned_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_assignments_current_rider ON bike_assignments (tenant_id, rider_id) WHERE returned_at IS NULL;

CREATE TABLE IF NOT EXISTS jobs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    pickup_location varchar(240) NOT NULL,
    dropoff_location varchar(240) NOT NULL,
    instructions varchar(2000) NULL,
    amount_ugx bigint NOT NULL CHECK (amount_ugx >= 0),
    deadline timestamptz NOT NULL,
    rider_id uuid NULL,
    status varchar(24) NOT NULL DEFAULT 'Unassigned',
    accepted_at timestamptz NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid NOT NULL,
    CONSTRAINT fk_jobs_rider FOREIGN KEY (tenant_id, rider_id) REFERENCES riders(tenant_id, id),
    CONSTRAINT fk_jobs_creator FOREIGN KEY (tenant_id, created_by_user_id) REFERENCES fleet_users(tenant_id, id)
);
CREATE INDEX IF NOT EXISTS ix_jobs_tenant_status_deadline ON jobs (tenant_id, status, deadline);
CREATE INDEX IF NOT EXISTS ix_jobs_tenant_rider_status ON jobs (tenant_id, rider_id, status);

CREATE TABLE IF NOT EXISTS reports (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    rider_id uuid NOT NULL,
    motorcycle_id uuid NULL,
    issue_type varchar(32) NOT NULL,
    description varchar(4000) NOT NULL,
    latitude double precision NULL,
    longitude double precision NULL,
    status varchar(24) NOT NULL DEFAULT 'Open',
    assigned_technician_user_id uuid NULL,
    resolution varchar(4000) NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    resolved_at timestamptz NULL,
    CONSTRAINT fk_reports_rider FOREIGN KEY (tenant_id, rider_id) REFERENCES riders(tenant_id, id),
    CONSTRAINT fk_reports_motorcycle FOREIGN KEY (tenant_id, motorcycle_id) REFERENCES motorcycles(tenant_id, id),
    CONSTRAINT fk_reports_technician FOREIGN KEY (tenant_id, assigned_technician_user_id) REFERENCES fleet_users(tenant_id, id),
    CONSTRAINT uq_reports_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT ck_reports_location CHECK (
        (latitude IS NULL AND longitude IS NULL) OR
        (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180)
    )
);
CREATE INDEX IF NOT EXISTS ix_reports_tenant_status_time ON reports (tenant_id, status, created_at DESC);

CREATE TABLE IF NOT EXISTS report_media (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    report_id uuid NOT NULL,
    object_key varchar(1024) NOT NULL,
    content_type varchar(120) NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes > 0),
    kind varchar(16) NOT NULL CHECK (kind IN ('Photo', 'Voice')),
    duration_seconds integer NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_report_media_report FOREIGN KEY (tenant_id, report_id) REFERENCES reports(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_report_voice_duration CHECK (kind <> 'Voice' OR duration_seconds BETWEEN 1 AND 120)
);
CREATE INDEX IF NOT EXISTS ix_report_media_tenant_report ON report_media (tenant_id, report_id);

CREATE TABLE IF NOT EXISTS maintenance (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    motorcycle_id uuid NOT NULL,
    service_type varchar(80) NOT NULL,
    due_at_mileage_km double precision NOT NULL DEFAULT 0,
    completed_at_mileage_km double precision NULL,
    cost_ugx bigint NOT NULL DEFAULT 0 CHECK (cost_ugx >= 0),
    status varchar(24) NOT NULL DEFAULT 'Scheduled',
    due_at timestamptz NULL,
    completed_at timestamptz NULL,
    notes varchar(2000) NULL,
    CONSTRAINT fk_maintenance_motorcycle FOREIGN KEY (tenant_id, motorcycle_id) REFERENCES motorcycles(tenant_id, id)
);
CREATE INDEX IF NOT EXISTS ix_maintenance_tenant_status_due ON maintenance (tenant_id, status, due_at);
CREATE INDEX IF NOT EXISTS ix_maintenance_tenant_bike_mileage ON maintenance (tenant_id, motorcycle_id, due_at_mileage_km);

CREATE TABLE IF NOT EXISTS notifications (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    rider_id uuid NOT NULL,
    type varchar(32) NOT NULL,
    title varchar(160) NOT NULL,
    body varchar(2000) NOT NULL,
    status varchar(16) NOT NULL DEFAULT 'Unread' CHECK (status IN ('Unread', 'Read', 'Archived')),
    created_at timestamptz NOT NULL DEFAULT now(),
    read_at timestamptz NULL,
    archived_at timestamptz NULL,
    CONSTRAINT fk_notifications_rider FOREIGN KEY (tenant_id, rider_id) REFERENCES riders(tenant_id, id)
);
CREATE INDEX IF NOT EXISTS ix_notifications_tenant_rider_status_time ON notifications (tenant_id, rider_id, status, created_at DESC);

CREATE TABLE IF NOT EXISTS gps_logs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    motorcycle_id uuid NOT NULL,
    latitude double precision NOT NULL CHECK (latitude BETWEEN -90 AND 90),
    longitude double precision NOT NULL CHECK (longitude BETWEEN -180 AND 180),
    speed_kph double precision NULL CHECK (speed_kph IS NULL OR speed_kph >= 0),
    mileage_km double precision NULL CHECK (mileage_km IS NULL OR mileage_km >= 0),
    heading_degrees double precision NULL CHECK (heading_degrees IS NULL OR heading_degrees BETWEEN 0 AND 360),
    recorded_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_gps_logs_motorcycle FOREIGN KEY (tenant_id, motorcycle_id) REFERENCES motorcycles(tenant_id, id)
);
CREATE INDEX IF NOT EXISTS ix_gps_logs_tenant_bike_recorded ON gps_logs (tenant_id, motorcycle_id, recorded_at DESC);

CREATE TABLE IF NOT EXISTS audit_logs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    user_id uuid NULL,
    action varchar(120) NOT NULL,
    entity_type varchar(120) NOT NULL,
    entity_id varchar(120) NULL,
    data_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT fk_audit_user FOREIGN KEY (tenant_id, user_id) REFERENCES fleet_users(tenant_id, id)
);
CREATE INDEX IF NOT EXISTS ix_audit_tenant_created ON audit_logs (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_tenant_entity ON audit_logs (tenant_id, entity_type, entity_id);

CREATE TABLE IF NOT EXISTS rider_devices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    rider_id uuid NOT NULL,
    fcm_token varchar(4096) NOT NULL,
    platform varchar(16) NOT NULL,
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_rider_devices_token UNIQUE (fcm_token),
    CONSTRAINT fk_rider_devices_rider FOREIGN KEY (tenant_id, rider_id) REFERENCES riders(tenant_id, id)
);
