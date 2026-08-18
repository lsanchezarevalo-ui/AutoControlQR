using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using static Helpers;

public static class VehicleEndpoints
{
    public static void MapVehicleEndpoints(this WebApplication app, string connectionString)
    {
        app.MapGet("/api/v1/vehicles", async (ClaimsPrincipal principal, string? search) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            try
            {
                var companyId = CompanyId(principal);
                await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync();
                var sql = @"SELECT v.id,v.plate,v.internal_number,v.brand,v.model,v.variant,v.current_mileage,v.mileage_updated_at,q.token,
                                   CASE WHEN COALESCE(mp.is_vehicle_specific,false) THEN NULL ELSE mp.name END,
                                   CASE WHEN COALESCE(mp.is_vehicle_specific,false) THEN NULL ELSE mpv.version_number END,
                                   CASE WHEN COALESCE(mp.is_vehicle_specific,false) THEN NULL ELSE a.plan_version_id END
                            FROM vehicles v
                            LEFT JOIN vehicle_qr_tokens q ON q.vehicle_id=v.id AND q.status='ACTIVE'
                            LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
                            LEFT JOIN maintenance_plan_versions mpv ON mpv.id=a.plan_version_id
                            LEFT JOIN maintenance_plans mp ON mp.id=mpv.maintenance_plan_id
                            WHERE v.company_id=@company AND v.status='ACTIVE'
                              AND (@search IS NULL OR v.plate ILIKE '%'||@search||'%' OR v.internal_number ILIKE '%'||@search||'%')
                            ORDER BY v.internal_number NULLS LAST, v.plate LIMIT 200";
                await using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("company", companyId);
                cmd.Parameters.Add("search", NpgsqlDbType.Text).Value = (object?)search ?? DBNull.Value;
                await using var r = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await r.ReadAsync()) list.Add(new {
                    id=r.GetGuid(0), plate=r.GetString(1), internalNumber=r.IsDBNull(2)?null:r.GetString(2), brand=r.GetString(3), model=r.GetString(4), variant=r.IsDBNull(5)?null:r.GetString(5),
                    currentMileage=r.GetInt32(6), mileageUpdatedAt=r.GetDateTime(7), qrToken=r.IsDBNull(8)?null:r.GetString(8),
                    planName=r.IsDBNull(9)?null:r.GetString(9), planVersion=r.IsDBNull(10)?(int?)null:r.GetInt32(10), planVersionId=r.IsDBNull(11)?(Guid?)null:r.GetGuid(11)
                });
                return Results.Ok(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error loading vehicles");
                return Results.Json(new { success=false, error=new { code="VEHICLES_LOAD_FAILED", message="No fue posible cargar la lista de vehículos." } }, statusCode:500);
            }
        }).RequireAuthorization();

        app.MapPost("/api/v1/vehicles", async (ClaimsPrincipal principal, CreateVehicleRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Plate)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
                return Results.BadRequest(new{success=false,error=new{message="Placa, marca y modelo son obligatorios."}});
            if(req.CurrentMileage<0)
                return Results.BadRequest(new{success=false,error=new{message="El kilometraje no puede ser negativo."}});
            var companyId = CompanyId(principal); var userId = UserId(principal);
            await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync(); await using var tx = await con.BeginTransactionAsync();
            try
            {
                if(req.PlanVersionId.HasValue)
                {
                    await using var planCheck=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@pv AND p.company_id=@c AND p.status='ACTIVE' AND pv.status='PUBLISHED'",con,tx);
                    planCheck.Parameters.AddWithValue("pv",req.PlanVersionId.Value);planCheck.Parameters.AddWithValue("c",companyId);
                    if(await planCheck.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="El plan seleccionado no es válido."}});}
                }
                var vehicleId = Guid.NewGuid();
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
                var now = DateTime.UtcNow;
                await using (var cmd = new NpgsqlCommand(@"INSERT INTO vehicles(id,company_id,plate,internal_number,brand,model,variant,current_mileage,mileage_updated_at) VALUES(@id,@company,@plate,@internal,@brand,@model,@variant,@km,@now)", con, tx))
                { cmd.Parameters.AddWithValue("id",vehicleId); cmd.Parameters.AddWithValue("company",companyId); cmd.Parameters.AddWithValue("plate",req.Plate.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue("internal",(object?)req.InternalNumber?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue("brand",req.Brand.Trim()); cmd.Parameters.AddWithValue("model",req.Model.Trim()); cmd.Parameters.AddWithValue("variant",(object?)req.Variant?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue("km",req.CurrentMileage); cmd.Parameters.AddWithValue("now",now); await cmd.ExecuteNonQueryAsync(); }
                await using (var cmd = new NpgsqlCommand("INSERT INTO mileage_readings(vehicle_id,mileage,source,created_by_user_id) VALUES(@v,@km,'INITIAL',@u)",con,tx))
                { cmd.Parameters.AddWithValue("v",vehicleId); cmd.Parameters.AddWithValue("km",req.CurrentMileage); cmd.Parameters.AddWithValue("u",userId); await cmd.ExecuteNonQueryAsync(); }
                await using (var cmd = new NpgsqlCommand("INSERT INTO vehicle_qr_tokens(vehicle_id,token) VALUES(@v,@t)",con,tx))
                { cmd.Parameters.AddWithValue("v",vehicleId); cmd.Parameters.AddWithValue("t",token); await cmd.ExecuteNonQueryAsync(); }
                if(req.PlanVersionId.HasValue)
                {
                    await using var cmd = new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx);
                    cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("p",req.PlanVersionId.Value);cmd.Parameters.AddWithValue("u",userId);await cmd.ExecuteNonQueryAsync();
                }
                await using (var cmd = new NpgsqlCommand("INSERT INTO audit_logs(company_id,user_id,entity_type,entity_id,action,new_values,source) VALUES(@c,@u,'VEHICLE',@v,'CREATE',CAST(@json AS jsonb),'WEB')",con,tx))
                { cmd.Parameters.AddWithValue("c",companyId); cmd.Parameters.AddWithValue("u",userId); cmd.Parameters.AddWithValue("v",vehicleId); cmd.Parameters.AddWithValue("json",JsonSerializer.Serialize(req)); await cmd.ExecuteNonQueryAsync(); }
                await tx.CommitAsync();
                return Results.Created($"/api/v1/vehicles/{vehicleId}", new { success=true, data=new { id=vehicleId, plate=req.Plate.Trim().ToUpperInvariant(), internalNumber=req.InternalNumber, currentMileage=req.CurrentMileage, qrToken=token } });
            }
            catch(PostgresException ex) when (ex.SqlState == "23505") { await tx.RollbackAsync(); return Results.Conflict(new {success=false,error=new{code="DUPLICATE_VEHICLE",message="La placa o número interno ya está registrado."}}); }
        }).RequireAuthorization();

        app.MapGet("/api/v1/vehicles/{vehicleId:guid}/qr-label", async (ClaimsPrincipal principal,Guid vehicleId) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT v.plate,v.internal_number,v.brand,v.model,v.variant,q.token,c.name,c.logo_data_url
              FROM vehicles v JOIN vehicle_qr_tokens q ON q.vehicle_id=v.id AND q.status='ACTIVE'
              JOIN companies c ON c.id=v.company_id WHERE v.id=@v AND v.company_id=@c AND v.status='ACTIVE' LIMIT 1";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));
            await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound();
            return Results.Ok(new{success=true,data=new{plate=r.GetString(0),internalNumber=r.IsDBNull(1)?null:r.GetString(1),brand=r.GetString(2),model=r.GetString(3),variant=r.IsDBNull(4)?null:r.GetString(4),qrToken=r.GetString(5),companyName=r.GetString(6),logoDataUrl=r.IsDBNull(7)?null:r.GetString(7)}});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/vehicles/{vehicleId:guid}", async (ClaimsPrincipal principal, Guid vehicleId, UpdateVehicleRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Plate)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
                return Results.BadRequest(new{success=false,error=new{message="Placa, marca y modelo son obligatorios."}});
            var plate=req.Plate.Trim().ToUpperInvariant();
            var internalNumber=string.IsNullOrWhiteSpace(req.InternalNumber)?null:req.InternalNumber.Trim().ToUpperInvariant();
            var brand=ToTitleCase(req.Brand);var model=ToTitleCase(req.Model);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            try{
              if(req.PlanVersionId.HasValue)
              {
                await using var planCheck=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@pv AND p.company_id=@c AND pv.status='PUBLISHED'",con,tx);
                planCheck.Parameters.AddWithValue("pv",req.PlanVersionId.Value);planCheck.Parameters.AddWithValue("c",CompanyId(principal));if(await planCheck.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="Selecciona un plan de mantenimiento válido."}});}
              }
              await using(var cmd=new NpgsqlCommand(@"UPDATE vehicles SET plate=@p,internal_number=@i,brand=@b,model=@m WHERE id=@v AND company_id=@c AND status='ACTIVE'",con,tx))
              {cmd.Parameters.AddWithValue("p",plate);cmd.Parameters.AddWithValue("i",(object?)internalNumber??DBNull.Value);cmd.Parameters.AddWithValue("b",brand);cmd.Parameters.AddWithValue("m",model);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
              Guid? currentPlan=null;
              await using(var cur=new NpgsqlCommand("SELECT plan_version_id FROM vehicle_plan_assignments WHERE vehicle_id=@v AND active=true LIMIT 1",con,tx))
              {cur.Parameters.AddWithValue("v",vehicleId);var x=await cur.ExecuteScalarAsync();if(x is Guid g)currentPlan=g;}
              if(currentPlan!=req.PlanVersionId)
              {
                await using(var close=new NpgsqlCommand("UPDATE vehicle_plan_assignments SET active=false,ends_at=now() WHERE vehicle_id=@v AND active=true",con,tx)){close.Parameters.AddWithValue("v",vehicleId);await close.ExecuteNonQueryAsync();}
                if(req.PlanVersionId.HasValue)
                {
                  await using(var ins=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx)){ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("p",req.PlanVersionId.Value);ins.Parameters.AddWithValue("u",UserId(principal));await ins.ExecuteNonQueryAsync();}
                }
              }
              await tx.CommitAsync();return Results.Ok(new{success=true});
            }catch(PostgresException ex) when(ex.SqlState=="23505"){await tx.RollbackAsync();return Results.Conflict(new{success=false,error=new{message="La placa o número interno ya está registrado."}});}
        }).RequireAuthorization();

        app.MapGet("/api/v1/vehicles/archived", async (ClaimsPrincipal principal) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
         await using var cmd=new NpgsqlCommand(@"SELECT id,plate,internal_number,brand,model,current_mileage FROM vehicles WHERE company_id=@c AND status='ARCHIVED' ORDER BY internal_number NULLS LAST,plate",con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
         await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),plate=r.GetString(1),internalNumber=r.IsDBNull(2)?null:r.GetString(2),brand=r.GetString(3),model=r.GetString(4),currentMileage=r.GetInt32(5)});
         return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/vehicles/{vehicleId:guid}/reactivate", async (ClaimsPrincipal principal,Guid vehicleId,ReactivateVehicleRequest req) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
         await using(var chk=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@p AND p.company_id=@c AND p.status='ACTIVE' AND pv.status='PUBLISHED'",con,tx)){chk.Parameters.AddWithValue("p",req.PlanVersionId);chk.Parameters.AddWithValue("c",CompanyId(principal));if(await chk.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="Selecciona un plan activo válido."}});}}
         await using(var cmd=new NpgsqlCommand("UPDATE vehicles SET status='ACTIVE' WHERE id=@v AND company_id=@c AND status='ARCHIVED'",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
         await using(var ins=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx)){ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("p",req.PlanVersionId);ins.Parameters.AddWithValue("u",UserId(principal));await ins.ExecuteNonQueryAsync();}
         var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
         await using(var qr=new NpgsqlCommand("INSERT INTO vehicle_qr_tokens(vehicle_id,token,status) VALUES(@v,@t,'ACTIVE')",con,tx)){qr.Parameters.AddWithValue("v",vehicleId);qr.Parameters.AddWithValue("t",token);await qr.ExecuteNonQueryAsync();}
         await tx.CommitAsync();return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/vehicles/{vehicleId:guid}/archive", async (ClaimsPrincipal principal,Guid vehicleId) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
         await using(var cmd=new NpgsqlCommand("UPDATE vehicles SET status='ARCHIVED' WHERE id=@v AND company_id=@c AND status='ACTIVE'",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
         await using(var cmd=new NpgsqlCommand("UPDATE vehicle_plan_assignments SET active=false,ends_at=now() WHERE vehicle_id=@v AND active=true",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);await cmd.ExecuteNonQueryAsync();}
         await using(var cmd=new NpgsqlCommand("UPDATE vehicle_qr_tokens SET status='REVOKED' WHERE vehicle_id=@v AND status='ACTIVE'",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);await cmd.ExecuteNonQueryAsync();}
         await tx.CommitAsync();return Results.Ok(new{success=true});
        }).RequireAuthorization();
    }
}
