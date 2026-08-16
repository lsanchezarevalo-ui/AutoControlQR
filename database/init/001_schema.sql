CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS companies (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name varchar(150) NOT NULL,
  exceptional_mileage_threshold integer NOT NULL DEFAULT 2000 CHECK (exceptional_mileage_threshold > 0),
  stale_mileage_days integer NOT NULL DEFAULT 7 CHECK (stale_mileage_days > 0),
  status varchar(20) NOT NULL DEFAULT 'ACTIVE',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id uuid REFERENCES companies(id),
  full_name varchar(150) NOT NULL,
  email varchar(200) NOT NULL UNIQUE,
  password_hash text NOT NULL,
  role varchar(50) NOT NULL,
  status varchar(20) NOT NULL DEFAULT 'ACTIVE',
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS vehicles (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id uuid NOT NULL REFERENCES companies(id),
  plate varchar(20) NOT NULL,
  internal_number varchar(50),
  brand varchar(100) NOT NULL,
  model varchar(100) NOT NULL,
  variant varchar(100),
  current_mileage integer NOT NULL CHECK (current_mileage >= 0),
  mileage_updated_at timestamptz NOT NULL DEFAULT now(),
  status varchar(20) NOT NULL DEFAULT 'ACTIVE',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_vehicle_plate UNIQUE (company_id, plate)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_internal_number
  ON vehicles(company_id, internal_number)
  WHERE internal_number IS NOT NULL;

CREATE TABLE IF NOT EXISTS vehicle_qr_tokens (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  vehicle_id uuid NOT NULL REFERENCES vehicles(id),
  token varchar(128) NOT NULL UNIQUE,
  status varchar(20) NOT NULL DEFAULT 'ACTIVE',
  created_at timestamptz NOT NULL DEFAULT now(),
  revoked_at timestamptz
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_active_qr_per_vehicle
  ON vehicle_qr_tokens(vehicle_id)
  WHERE status = 'ACTIVE';

CREATE TABLE IF NOT EXISTS mileage_readings (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  vehicle_id uuid NOT NULL REFERENCES vehicles(id),
  mileage integer NOT NULL CHECK (mileage >= 0),
  source varchar(30) NOT NULL,
  is_exceptional boolean NOT NULL DEFAULT false,
  exception_confirmed boolean NOT NULL DEFAULT false,
  created_by_user_id uuid REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_mileage_vehicle_created ON mileage_readings(vehicle_id, created_at DESC);

CREATE TABLE IF NOT EXISTS audit_logs (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id uuid REFERENCES companies(id),
  user_id uuid REFERENCES users(id),
  entity_type varchar(50) NOT NULL,
  entity_id uuid NOT NULL,
  action varchar(50) NOT NULL,
  old_values jsonb,
  new_values jsonb,
  source varchar(30) NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
