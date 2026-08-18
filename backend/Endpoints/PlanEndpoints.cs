using System.Security.Claims;
using Npgsql;
using static Helpers;

public static class PlanEndpoints
{
    public static void MapPlanEndpoints(this WebApplication app, string connectionString)
    {
        // PLANES
        app.MapGet("/api/v1/maintenance-plans", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId = CompanyId(principal);
            await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync();
            var sql = @"SELECT mp.id, mp.name, mp.brand, mp.model, mp.variant, mpv.id, mpv.version_number, mpv.status
                        FROM maintenance_plans mp
                        JOIN maintenance_plan_versions mpv ON mpv.maintenance_plan_id=mp.id
                        WHERE mp.company_id=@c AND mp.status='ACTIVE' AND COALESCE(mp.is_vehicle_specific,false)=false
                        ORDER BY mp.name, mpv.version_number DESC";
            await using var cmd = new NpgsqlCommand(sql, con); cmd.Parameters.AddWithValue("c", companyId);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<object>();
            while(await r.ReadAsync()) list.Add(new { id=r.GetGuid(0), name=r.GetString(1), brand=r.GetString(2), model=r.GetString(3), variant=r.IsDBNull(4)?null:r.GetString(4), versionId=r.GetGuid(5), versionNumber=r.GetInt32(6), status=r.GetString(7) });
            return Results.Ok(new { success=true, data=list });
        }).RequireAuthorization();

        app.MapPost("/api/v1/maintenance-plans", async (ClaimsPrincipal principal, CreatePlanRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
                return Results.BadRequest(new{success=false,error=new{message="Nombre, marca y modelo son obligatorios."}});
            var companyId=CompanyId(principal); var userId=UserId(principal);
            await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync(); await using var tx=await con.BeginTransactionAsync();
            try
            {
                var planId=Guid.NewGuid(); var versionId=Guid.NewGuid();
                await using(var c1=new NpgsqlCommand("INSERT INTO maintenance_plans(id,company_id,name,brand,model,variant) VALUES(@id,@c,@n,@b,@m,@v)",con,tx))
                { c1.Parameters.AddWithValue("id",planId);c1.Parameters.AddWithValue("c",companyId);c1.Parameters.AddWithValue("n",req.Name.Trim());c1.Parameters.AddWithValue("b",req.Brand.Trim());c1.Parameters.AddWithValue("m",req.Model.Trim());c1.Parameters.AddWithValue("v",(object?)req.Variant?.Trim()??DBNull.Value);await c1.ExecuteNonQueryAsync();}
                await using(var c2=new NpgsqlCommand("INSERT INTO maintenance_plan_versions(id,maintenance_plan_id,version_number,status,published_at,created_by_user_id) VALUES(@id,@p,1,'PUBLISHED',now(),@u)",con,tx))
                {c2.Parameters.AddWithValue("id",versionId);c2.Parameters.AddWithValue("p",planId);c2.Parameters.AddWithValue("u",userId);await c2.ExecuteNonQueryAsync();}
                await tx.CommitAsync();
                return Results.Ok(new {success=true,data=new{id=planId,versionId,versionNumber=1}});
            }
            catch(PostgresException ex) when(ex.SqlState=="23505") { await tx.RollbackAsync(); return Results.Conflict(new{success=false,error=new{message="Ya existe un plan con ese nombre."}});}
        }).RequireAuthorization();

        app.MapPatch("/api/v1/maintenance-plans/{planId:guid}", async (ClaimsPrincipal principal, Guid planId, UpdatePlanRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
                return Results.BadRequest(new{success=false,error=new{message="Nombre, marca y modelo son obligatorios."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            try{
              await using var cmd=new NpgsqlCommand("UPDATE maintenance_plans SET name=@n,brand=@b,model=@m WHERE id=@p AND company_id=@c",con);
              cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("b",ToTitleCase(req.Brand));cmd.Parameters.AddWithValue("m",ToTitleCase(req.Model));cmd.Parameters.AddWithValue("p",planId);cmd.Parameters.AddWithValue("c",CompanyId(principal));
              var n=await cmd.ExecuteNonQueryAsync();if(n==0)return Results.NotFound();
              return Results.Ok(new{success=true});
            }catch(PostgresException ex) when(ex.SqlState=="23505"){return Results.Conflict(new{success=false,error=new{message="Ya existe un plan con ese nombre."}});}
        }).RequireAuthorization();

        app.MapGet("/api/v1/plan-versions/{versionId:guid}/services", async (ClaimsPrincipal principal, Guid versionId) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT s.id,s.name,s.category,s.specification,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days
                      FROM maintenance_plan_services s
                      JOIN maintenance_plan_versions pv ON pv.id=s.plan_version_id
                      JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id
                      WHERE s.plan_version_id=@v AND p.company_id=@c AND s.active=true ORDER BY s.sort_order,s.name";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",versionId);cmd.Parameters.AddWithValue("c",companyId);
            await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync()) list.Add(ServiceObject(r));
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPost("/api/v1/plan-versions/{versionId:guid}/services", async (ClaimsPrincipal principal, Guid versionId, CreateServiceRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
            if(string.IsNullOrWhiteSpace(req.Category))return Results.BadRequest(new{success=false,error=new{message="La categoría es obligatoria."}});
            if(req.IntervalKm is null && req.IntervalMonths is null) return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
            if(req.IntervalKm.HasValue && req.IntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
            if(req.PrealertKm.HasValue && req.PrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
            var companyId=CompanyId(principal);var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using(var verify=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@v AND p.company_id=@c",con))
            {verify.Parameters.AddWithValue("v",versionId);verify.Parameters.AddWithValue("c",companyId);if(await verify.ExecuteScalarAsync() is null)return Results.NotFound();}
            var companyServiceId=await ResolveOrCreateCompanyService(con,null,companyId,userId,req.Name,req.Category,req.Specification,req.IntervalKm,req.IntervalMonths,req.PrealertKm,req.PrealertDays);
            var id=Guid.NewGuid();
            await using var cmd=new NpgsqlCommand(@"INSERT INTO maintenance_plan_services(id,plan_version_id,company_service_id,name,category,specification,interval_km,interval_months,prealert_km,prealert_days)
                                                   VALUES(@id,@v,@cs,@n,@cat,@spec,@ikm,@imon,@pkm,@pday)",con);
            cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("v",versionId);cmd.Parameters.AddWithValue("cs",companyServiceId);cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("cat",req.Category.Trim());
            cmd.Parameters.AddWithValue("spec",(object?)req.Specification?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("ikm",(object?)req.IntervalKm??DBNull.Value);cmd.Parameters.AddWithValue("imon",(object?)req.IntervalMonths??DBNull.Value);cmd.Parameters.AddWithValue("pkm",(object?)req.PrealertKm??DBNull.Value);cmd.Parameters.AddWithValue("pday",(object?)req.PrealertDays??DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new{success=true,data=new{id}});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/plan-services/{serviceId:guid}", async (ClaimsPrincipal principal, Guid serviceId, UpdateServiceRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
            if(req.IntervalKm is null && req.IntervalMonths is null)return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
            if(req.IntervalKm.HasValue && req.IntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
            if(req.PrealertKm.HasValue && req.PrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
            var companyId=CompanyId(principal);var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            string category;
            await using(var cc=new NpgsqlCommand(@"SELECT s.category FROM maintenance_plan_services s JOIN maintenance_plan_versions pv ON pv.id=s.plan_version_id JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE s.id=@id AND p.company_id=@c AND s.active=true",con))
            {cc.Parameters.AddWithValue("id",serviceId);cc.Parameters.AddWithValue("c",companyId);var x=await cc.ExecuteScalarAsync();if(x is null)return Results.NotFound();category=(string)x;}
            var companyServiceId=await ResolveOrCreateCompanyService(con,null,companyId,userId,req.Name,category,req.Specification,req.IntervalKm,req.IntervalMonths,req.PrealertKm,req.PrealertDays);
            var sql=@"UPDATE maintenance_plan_services s SET name=@n,company_service_id=@cs,specification=@spec,interval_km=@ikm,interval_months=@imon,prealert_km=@pkm,prealert_days=@pday
                      FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id
                      WHERE s.id=@id AND s.plan_version_id=pv.id AND p.company_id=@c AND s.active=true";
            await using var cmd=new NpgsqlCommand(sql,con);
            cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("cs",companyServiceId);cmd.Parameters.AddWithValue("spec",(object?)req.Specification?.Trim()??DBNull.Value);
            cmd.Parameters.AddWithValue("ikm",(object?)req.IntervalKm??DBNull.Value);cmd.Parameters.AddWithValue("imon",(object?)req.IntervalMonths??DBNull.Value);
            cmd.Parameters.AddWithValue("pkm",(object?)req.PrealertKm??DBNull.Value);cmd.Parameters.AddWithValue("pday",(object?)req.PrealertDays??DBNull.Value);
            cmd.Parameters.AddWithValue("id",serviceId);cmd.Parameters.AddWithValue("c",companyId);
            if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPost("/api/v1/vehicles/{vehicleId:guid}/assign-plan", async (ClaimsPrincipal principal, Guid vehicleId, AssignPlanRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal); var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            var check=@"SELECT 1 FROM vehicles v CROSS JOIN maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE v.id=@vehicle AND v.company_id=@c AND pv.id=@version AND p.company_id=@c";
            await using(var c=new NpgsqlCommand(check,con,tx)){c.Parameters.AddWithValue("vehicle",vehicleId);c.Parameters.AddWithValue("version",req.PlanVersionId);c.Parameters.AddWithValue("c",companyId);if(await c.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.NotFound();}}
            await using(var close=new NpgsqlCommand("UPDATE vehicle_plan_assignments SET active=false,ends_at=now() WHERE vehicle_id=@v AND active=true",con,tx)){close.Parameters.AddWithValue("v",vehicleId);await close.ExecuteNonQueryAsync();}
            await using(var ins=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx)){ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("p",req.PlanVersionId);ins.Parameters.AddWithValue("u",userId);await ins.ExecuteNonQueryAsync();}
            await tx.CommitAsync();return Results.Ok(new{success=true});
        }).RequireAuthorization();

        // SERVICIOS INDIVIDUALES POR VEHÍCULO V31.3
        app.MapPost("/api/v1/vehicles/{vehicleId:guid}/individual-services", async (ClaimsPrincipal principal, Guid vehicleId, CreateServiceRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
            if(req.IntervalKm is null && req.IntervalMonths is null)return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
            if(req.IntervalKm.HasValue && req.IntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
            if(req.PrealertKm.HasValue && req.PrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
            var companyId=CompanyId(principal);var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            try
            {
                string brand,model;Guid? activeVersion=null;bool activeIsIndividual=false;
                await using(var vc=new NpgsqlCommand(@"SELECT v.brand,v.model,a.plan_version_id,COALESCE(mp.is_vehicle_specific,false)
                  FROM vehicles v
                  LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
                  LEFT JOIN maintenance_plan_versions pv ON pv.id=a.plan_version_id
                  LEFT JOIN maintenance_plans mp ON mp.id=pv.maintenance_plan_id
                  WHERE v.id=@v AND v.company_id=@c FOR UPDATE OF v",con,tx))
                {
                    vc.Parameters.AddWithValue("v",vehicleId);vc.Parameters.AddWithValue("c",companyId);
                    await using var r=await vc.ExecuteReaderAsync();
                    if(!await r.ReadAsync()){await tx.RollbackAsync();return Results.NotFound();}
                    brand=r.GetString(0);model=r.GetString(1);activeVersion=r.IsDBNull(2)?null:r.GetGuid(2);activeIsIndividual=r.GetBoolean(3);
                }
                if(activeVersion.HasValue && !activeIsIndividual){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="Este vehículo ya usa un Plan de Mantenimiento. Edita los servicios desde el plan."}});}
                Guid versionId;
                if(activeVersion.HasValue) versionId=activeVersion.Value;
                else
                {
                    var planId=Guid.NewGuid();versionId=Guid.NewGuid();
                    await using(var p=new NpgsqlCommand(@"INSERT INTO maintenance_plans(id,company_id,name,brand,model,status,is_vehicle_specific,vehicle_id)
                      VALUES(@id,@c,@n,@b,@m,'ACTIVE',true,@v)",con,tx))
                    {p.Parameters.AddWithValue("id",planId);p.Parameters.AddWithValue("c",companyId);p.Parameters.AddWithValue("n",$"__VEHICLE_{vehicleId:N}");p.Parameters.AddWithValue("b",brand);p.Parameters.AddWithValue("m",model);p.Parameters.AddWithValue("v",vehicleId);await p.ExecuteNonQueryAsync();}
                    await using(var pv=new NpgsqlCommand(@"INSERT INTO maintenance_plan_versions(id,maintenance_plan_id,version_number,status,published_at,created_by_user_id)
                      VALUES(@id,@p,1,'PUBLISHED',now(),@u)",con,tx))
                    {pv.Parameters.AddWithValue("id",versionId);pv.Parameters.AddWithValue("p",planId);pv.Parameters.AddWithValue("u",userId);await pv.ExecuteNonQueryAsync();}
                    await using(var a=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx))
                    {a.Parameters.AddWithValue("v",vehicleId);a.Parameters.AddWithValue("p",versionId);a.Parameters.AddWithValue("u",userId);await a.ExecuteNonQueryAsync();}
                }
                var companyServiceId=await ResolveOrCreateCompanyService(con,tx,companyId,userId,req.Name,req.Category,req.Specification,req.IntervalKm,req.IntervalMonths,req.PrealertKm,req.PrealertDays);
                var serviceId=Guid.NewGuid();
                await using(var sc=new NpgsqlCommand(@"INSERT INTO maintenance_plan_services(id,plan_version_id,company_service_id,name,category,specification,interval_km,interval_months,prealert_km,prealert_days)
                  VALUES(@id,@pv,@cs,@n,@cat,@sp,@ik,@im,@pk,@pd)",con,tx))
                {
                    sc.Parameters.AddWithValue("id",serviceId);sc.Parameters.AddWithValue("pv",versionId);sc.Parameters.AddWithValue("cs",companyServiceId);sc.Parameters.AddWithValue("n",req.Name.Trim());
                    sc.Parameters.AddWithValue("cat",string.IsNullOrWhiteSpace(req.Category)?"General":req.Category.Trim());
                    sc.Parameters.AddWithValue("sp",(object?)req.Specification?.Trim()??DBNull.Value);sc.Parameters.AddWithValue("ik",(object?)req.IntervalKm??DBNull.Value);
                    sc.Parameters.AddWithValue("im",(object?)req.IntervalMonths??DBNull.Value);sc.Parameters.AddWithValue("pk",(object?)req.PrealertKm??DBNull.Value);
                    sc.Parameters.AddWithValue("pd",(object?)req.PrealertDays??DBNull.Value);await sc.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();return Results.Ok(new{success=true,data=new{id=serviceId}});
            }
            catch(Exception ex){await tx.RollbackAsync();app.Logger.LogError(ex,"Create individual vehicle service failed");return Results.Json(new{success=false,error=new{message="No fue posible crear el servicio del vehículo."}},statusCode:500);}
        }).RequireAuthorization();

        app.MapGet("/api/v1/maintenance-plans/archived", async (ClaimsPrincipal principal) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
         await using var cmd=new NpgsqlCommand("SELECT id,name,brand,model FROM maintenance_plans WHERE company_id=@c AND status='ARCHIVED' ORDER BY name",con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
         await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),name=r.GetString(1),brand=r.GetString(2),model=r.GetString(3)});
         return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/maintenance-plans/{planId:guid}/reactivate", async (ClaimsPrincipal principal,Guid planId) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var cmd=new NpgsqlCommand("UPDATE maintenance_plans SET status='ACTIVE' WHERE id=@p AND company_id=@c AND status='ARCHIVED'",con);cmd.Parameters.AddWithValue("p",planId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/maintenance-plans/{planId:guid}/archive", async (ClaimsPrincipal principal,Guid planId) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
         await using(var chk=new NpgsqlCommand(@"SELECT count(*) FROM vehicle_plan_assignments a JOIN vehicles v ON v.id=a.vehicle_id WHERE a.active=true AND v.status='ACTIVE' AND v.company_id=@c AND a.plan_version_id IN (SELECT id FROM maintenance_plan_versions WHERE maintenance_plan_id=@p)",con)){chk.Parameters.AddWithValue("p",planId);chk.Parameters.AddWithValue("c",CompanyId(principal));var count=Convert.ToInt32(await chk.ExecuteScalarAsync());if(count>0)return Results.Conflict(new{success=false,error=new{message=$"Este plan está asignado a {count} vehículo(s) activo(s). Cambia primero esos vehículos a otro plan."}});}
         await using var cmd=new NpgsqlCommand("UPDATE maintenance_plans SET status='ARCHIVED' WHERE id=@p AND company_id=@c AND status='ACTIVE'",con);cmd.Parameters.AddWithValue("p",planId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();
         return Results.Ok(new{success=true});
        }).RequireAuthorization();
    }
}
