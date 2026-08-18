using Microsoft.AspNetCore.Identity;
using Npgsql;

public static class Schema
{
    public static async Task EnsureDatabaseReady(string cs)
    {
        Exception? last=null;
        for(var i=1;i<=30;i++)
        {
            try{await using var con=new NpgsqlConnection(cs);await con.OpenAsync();Console.WriteLine($"Database connection ready on attempt {i}.");return;}
            catch(Exception ex){last=ex;Console.WriteLine($"Database not ready (attempt {i}/30): {ex.Message}");await Task.Delay(2000);}
        }
        throw new InvalidOperationException("Database did not become available.",last);
    }

    public static async Task EnsureV5Schema(string cs)
    {
        var sql=@"
CREATE TABLE IF NOT EXISTS maintenance_plans (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id), name varchar(150) NOT NULL,
 brand varchar(100) NOT NULL, model varchar(100) NOT NULL, variant varchar(100), status varchar(20) NOT NULL DEFAULT 'ACTIVE', created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), UNIQUE(company_id,name));
CREATE TABLE IF NOT EXISTS maintenance_plan_versions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), maintenance_plan_id uuid NOT NULL REFERENCES maintenance_plans(id), version_number integer NOT NULL,
 status varchar(20) NOT NULL DEFAULT 'PUBLISHED', published_at timestamptz, created_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(maintenance_plan_id,version_number));
CREATE TABLE IF NOT EXISTS maintenance_plan_services (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), plan_version_id uuid NOT NULL REFERENCES maintenance_plan_versions(id), name varchar(150) NOT NULL, category varchar(100) NOT NULL,
 specification varchar(300), interval_km integer, interval_months integer, prealert_km integer, prealert_days integer, active boolean NOT NULL DEFAULT true, sort_order integer NOT NULL DEFAULT 0, created_at timestamptz NOT NULL DEFAULT now(), CHECK(interval_km IS NOT NULL OR interval_months IS NOT NULL));
CREATE TABLE IF NOT EXISTS vehicle_plan_assignments (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), vehicle_id uuid NOT NULL REFERENCES vehicles(id), plan_version_id uuid NOT NULL REFERENCES maintenance_plan_versions(id), starts_at timestamptz NOT NULL DEFAULT now(), ends_at timestamptz, active boolean NOT NULL DEFAULT true, assigned_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now());
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_active_plan ON vehicle_plan_assignments(vehicle_id) WHERE active=true;
CREATE TABLE IF NOT EXISTS vehicle_service_baselines (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), vehicle_id uuid NOT NULL REFERENCES vehicles(id), plan_service_id uuid NOT NULL REFERENCES maintenance_plan_services(id), last_service_mileage integer, last_service_date date, source varchar(30) NOT NULL DEFAULT 'ADMIN', created_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(vehicle_id,plan_service_id));";
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
    }

    public static async Task EnsureV6Schema(string cs)
    {
        var sql=@"
CREATE TABLE IF NOT EXISTS maintenance_records (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id), vehicle_id uuid NOT NULL REFERENCES vehicles(id), technician_user_id uuid REFERENCES users(id), service_date date NOT NULL, mileage integer NOT NULL, notes text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS maintenance_record_items (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), maintenance_record_id uuid NOT NULL REFERENCES maintenance_records(id), plan_service_id uuid NOT NULL REFERENCES maintenance_plan_services(id), service_name_snapshot varchar(150) NOT NULL, specification_snapshot varchar(300), interval_km_snapshot integer, interval_months_snapshot integer, next_due_mileage integer, next_due_date date, created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS ix_maintenance_records_vehicle_date ON maintenance_records(vehicle_id,service_date DESC);
CREATE INDEX IF NOT EXISTS ix_maintenance_items_record ON maintenance_record_items(maintenance_record_id);";
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
    }


    public static async Task EnsureV12Schema(string cs)
    {
        var sql=@"
ALTER TABLE companies ADD COLUMN IF NOT EXISTS code varchar(40);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS status varchar(20) NOT NULL DEFAULT 'ACTIVE';
ALTER TABLE companies ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
CREATE UNIQUE INDEX IF NOT EXISTS uq_companies_code ON companies(lower(code)) WHERE code IS NOT NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS legal_name varchar(180);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS tax_id varchar(80);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS phone varchar(80);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS email varchar(180);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS address varchar(250);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS logo_data_url text;
CREATE SEQUENCE IF NOT EXISTS company_code_seq START 1;
DO $$
DECLARE mx bigint;
BEGIN
 SELECT COALESCE(MAX(NULLIF(regexp_replace(code,'[^0-9]','','g'), '')::bigint),0) INTO mx FROM companies WHERE code LIKE 'EMP-%';
 IF mx >= (SELECT last_value FROM company_code_seq) THEN PERFORM setval('company_code_seq',mx+1,false); END IF;
END $$;
";
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
    }


    public static async Task EnsureV30Schema(string cs)
    {
        var sql=@"
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_enabled boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_internal boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_email_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_email varchar(180);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_due_soon boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_overdue boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_repeat_days integer NOT NULL DEFAULT 7;
CREATE TABLE IF NOT EXISTS notification_log(
 id uuid PRIMARY KEY,company_id uuid NOT NULL REFERENCES companies(id),vehicle_id uuid NOT NULL REFERENCES vehicles(id),
 plan_service_id uuid REFERENCES maintenance_plan_services(id),status varchar(30) NOT NULL,channel varchar(30) NOT NULL,
 recipient varchar(180),result varchar(30) NOT NULL DEFAULT 'RECORDED',created_by_user_id uuid REFERENCES users(id),
 created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS ix_notification_log_company_created ON notification_log(company_id,created_at DESC);
";
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
    }


    public static async Task EnsureV31IndividualServicesSchema(string cs)
    {
        var sql=@"
ALTER TABLE maintenance_plans ADD COLUMN IF NOT EXISTS is_vehicle_specific boolean NOT NULL DEFAULT false;
ALTER TABLE maintenance_plans ADD COLUMN IF NOT EXISTS vehicle_id uuid REFERENCES vehicles(id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_specific_plan ON maintenance_plans(vehicle_id) WHERE is_vehicle_specific=true AND vehicle_id IS NOT NULL;
";
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
    }

    public static async Task EnsureV32ServiceCatalogSchema(string cs)
    {
        var sql=@"
CREATE TABLE IF NOT EXISTS company_services (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id), name varchar(150) NOT NULL, category varchar(100) NOT NULL DEFAULT 'General',
 specification varchar(300), default_interval_km integer, default_interval_months integer, default_prealert_km integer, default_prealert_days integer,
 active boolean NOT NULL DEFAULT true, created_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now());
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_service_active_name ON company_services(company_id, lower(name)) WHERE active=true;
ALTER TABLE maintenance_plan_services ADD COLUMN IF NOT EXISTS company_service_id uuid REFERENCES company_services(id);
ALTER TABLE vehicle_service_baselines ADD COLUMN IF NOT EXISTS company_service_id uuid REFERENCES company_services(id);
ALTER TABLE maintenance_record_items ADD COLUMN IF NOT EXISTS company_service_id uuid REFERENCES company_services(id);

INSERT INTO company_services(company_id,name,category,specification,default_interval_km,default_interval_months,default_prealert_km,default_prealert_days)
SELECT DISTINCT ON (p.company_id, lower(s.name))
  p.company_id, s.name, s.category, s.specification, s.interval_km, s.interval_months, s.prealert_km, s.prealert_days
FROM maintenance_plan_services s
JOIN maintenance_plan_versions pv ON pv.id=s.plan_version_id
JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id
WHERE s.company_service_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM company_services cs WHERE cs.company_id=p.company_id AND lower(cs.name)=lower(s.name))
ORDER BY p.company_id, lower(s.name), s.created_at DESC;

UPDATE maintenance_plan_services s
SET company_service_id=cs.id
FROM maintenance_plan_versions pv, maintenance_plans p, company_services cs
WHERE s.plan_version_id=pv.id AND pv.maintenance_plan_id=p.id
  AND cs.company_id=p.company_id AND lower(cs.name)=lower(s.name)
  AND s.company_service_id IS NULL;

UPDATE vehicle_service_baselines b
SET company_service_id=s.company_service_id
FROM maintenance_plan_services s
WHERE b.plan_service_id=s.id AND b.company_service_id IS NULL AND s.company_service_id IS NOT NULL;

UPDATE maintenance_record_items i
SET company_service_id=s.company_service_id
FROM maintenance_plan_services s
WHERE i.plan_service_id=s.id AND i.company_service_id IS NULL AND s.company_service_id IS NOT NULL;
";
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);cmd.CommandTimeout=120;await cmd.ExecuteNonQueryAsync();
    }

    public static async Task EnsureDemoData(string cs)
    {
        await using var con=new NpgsqlConnection(cs);await con.OpenAsync();
        Guid companyId;
        await using(var cmd=new NpgsqlCommand("SELECT id FROM companies WHERE code='DEMO' LIMIT 1",con))
        {var x=await cmd.ExecuteScalarAsync();if(x is Guid g) companyId=g;else{companyId=Guid.NewGuid();await using var ins=new NpgsqlCommand("INSERT INTO companies(id,name,code,status) VALUES(@id,'Empresa Demo','DEMO','ACTIVE')",con);ins.Parameters.AddWithValue("id",companyId);await ins.ExecuteNonQueryAsync();}}
        await using(var cmd=new NpgsqlCommand("SELECT 1 FROM users WHERE email='admin@demo.local'",con))
        {if(await cmd.ExecuteScalarAsync() is null){var id=Guid.NewGuid();var demo=new DemoUser(id,companyId,"Administrador Demo","admin@demo.local","","COMPANY_ADMIN");var temp=Environment.GetEnvironmentVariable("AUTOCONTROLQR_DEMO_PASSWORD")??Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));Console.WriteLine($"[INITIAL CREDENTIAL] admin@demo.local password: {temp}");var hash=new PasswordHasher<DemoUser>().HashPassword(demo,temp);await using var ins=new NpgsqlCommand("INSERT INTO users(id,company_id,full_name,email,password_hash,role) VALUES(@id,@c,@n,@e,@p,@r)",con);ins.Parameters.AddWithValue("id",id);ins.Parameters.AddWithValue("c",companyId);ins.Parameters.AddWithValue("n",demo.FullName);ins.Parameters.AddWithValue("e",demo.Email);ins.Parameters.AddWithValue("p",hash);ins.Parameters.AddWithValue("r",demo.Role);await ins.ExecuteNonQueryAsync();}}
        await using(var cmd=new NpgsqlCommand("SELECT 1 FROM users WHERE email='platform@autocontrol.local'",con))
        {if(await cmd.ExecuteScalarAsync() is null){var id=Guid.NewGuid();var admin=new DemoUser(id,companyId,"Administrador Plataforma","platform@autocontrol.local","","PLATFORM_ADMIN");var temp=Environment.GetEnvironmentVariable("AUTOCONTROLQR_PLATFORM_PASSWORD")??Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));Console.WriteLine($"[INITIAL CREDENTIAL] platform@autocontrol.local password: {temp}");var hash=new PasswordHasher<DemoUser>().HashPassword(admin,temp);await using var ins=new NpgsqlCommand("INSERT INTO users(id,company_id,full_name,email,password_hash,role,status) VALUES(@id,@c,@n,@e,@p,@r,'ACTIVE')",con);ins.Parameters.AddWithValue("id",id);ins.Parameters.AddWithValue("c",companyId);ins.Parameters.AddWithValue("n",admin.FullName);ins.Parameters.AddWithValue("e",admin.Email);ins.Parameters.AddWithValue("p",hash);ins.Parameters.AddWithValue("r",admin.Role);await ins.ExecuteNonQueryAsync();}}
    }
}
