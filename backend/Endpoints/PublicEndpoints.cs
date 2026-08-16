using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using QRCoder;
using static Helpers;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this WebApplication app, string connectionString, string publicWebBaseUrl)
    {
        // PUBLIC QR / MILEAGE
        app.MapGet("/api/v1/public/v/{token}", async (string token) =>
        {
            await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync();
            var sql = @"SELECT v.id,v.plate,v.internal_number,v.brand,v.model,v.variant,v.current_mileage,v.mileage_updated_at,c.exceptional_mileage_threshold
                        FROM vehicle_qr_tokens q JOIN vehicles v ON v.id=q.vehicle_id JOIN companies c ON c.id=v.company_id
                        WHERE q.token=@token AND q.status='ACTIVE' AND v.status='ACTIVE' AND c.status='ACTIVE'";
            Guid vehicleId; string plate,brand,model; string? internalNumber,variant; int currentMileage,threshold; DateTime lastMileageUpdate;
            await using(var cmd = new NpgsqlCommand(sql,con))
            {
                cmd.Parameters.AddWithValue("token",token);
                await using var r=await cmd.ExecuteReaderAsync();
                if(!await r.ReadAsync()) return Results.NotFound(new{success=false,error=new{code="QR_NOT_FOUND",message="QR no válido o inactivo."}});
                vehicleId=r.GetGuid(0);plate=r.GetString(1);internalNumber=r.IsDBNull(2)?null:r.GetString(2);brand=r.GetString(3);model=r.GetString(4);variant=r.IsDBNull(5)?null:r.GetString(5);currentMileage=r.GetInt32(6);lastMileageUpdate=r.GetDateTime(7);threshold=r.GetInt32(8);
            }
            var services=new List<MaintenanceStatusItem>();
            var serviceSql=@"SELECT s.id,s.name,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days,b.last_service_mileage,b.last_service_date
                             FROM vehicle_plan_assignments a
                             JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id AND s.active=true
                             LEFT JOIN vehicle_service_baselines b ON b.vehicle_id=a.vehicle_id AND b.plan_service_id=s.id
                             WHERE a.vehicle_id=@v AND a.active=true ORDER BY s.sort_order,s.name";
            await using(var sc=new NpgsqlCommand(serviceSql,con))
            {
                sc.Parameters.AddWithValue("v",vehicleId);
                await using var r=await sc.ExecuteReaderAsync();
                while(await r.ReadAsync())
                {
                    var serviceId=r.GetGuid(0);var name=r.GetString(1);var intervalKm=r.IsDBNull(2)?(int?)null:r.GetInt32(2);var intervalMonths=r.IsDBNull(3)?(int?)null:r.GetInt32(3);var prealertKm=r.IsDBNull(4)?0:r.GetInt32(4);var prealertDays=r.IsDBNull(5)?0:r.GetInt32(5);var lastKm=r.IsDBNull(6)?(int?)null:r.GetInt32(6);var lastDate=r.IsDBNull(7)?(DateTime?)null:r.GetDateTime(7);
                    services.Add(CalcStatus(serviceId,name,currentMileage,intervalKm,intervalMonths,prealertKm,prealertDays,lastKm,lastDate));
                }
            }
            var overall=services.Count==0?"NO_PLAN":services.Any(x=>x.Status=="OVERDUE")?"OVERDUE":services.Any(x=>x.Status=="DUE_SOON")?"DUE_SOON":services.Any(x=>x.Status=="NO_BASELINE")?"NO_BASELINE":"UP_TO_DATE";
            return Results.Ok(new{success=true,data=new{vehicleId,plate,internalNumber,brand,model,variant,currentMileage,lastMileageUpdate,threshold,overallStatus=overall,services}});
        }).RequireRateLimiting("public");

        app.MapGet("/api/v1/public/qr/{token}.svg", async (string token) =>
        {
            await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT 1 FROM vehicle_qr_tokens q JOIN vehicles v ON v.id=q.vehicle_id JOIN companies c ON c.id=v.company_id WHERE q.token=@token AND q.status='ACTIVE' AND v.status='ACTIVE' AND c.status='ACTIVE'", con);
            cmd.Parameters.AddWithValue("token", token);
            if (await cmd.ExecuteScalarAsync() is null) return Results.NotFound();
            var url = $"{publicWebBaseUrl.TrimEnd('/')}/v/{token}";
            using var gen = new QRCodeGenerator(); using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q); var svgQr = new SvgQRCode(data); var svg = svgQr.GetGraphic(5);
            return Results.Text(svg, "image/svg+xml");
        }).RequireRateLimiting("public");

        app.MapPost("/api/v1/public/v/{token}/mileage", async (string token, MileageRequest req) =>
        {
            if(req.Mileage<0)return Results.BadRequest(new{success=false,error=new{code="INVALID_MILEAGE",message="Kilometraje no válido."}});
            await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync(); await using var tx = await con.BeginTransactionAsync();
            var sql = @"SELECT v.id,v.company_id,v.current_mileage,c.exceptional_mileage_threshold FROM vehicle_qr_tokens q JOIN vehicles v ON v.id=q.vehicle_id JOIN companies c ON c.id=v.company_id WHERE q.token=@token AND q.status='ACTIVE' AND v.status='ACTIVE' AND c.status='ACTIVE' FOR UPDATE OF v";
            await using var cmd = new NpgsqlCommand(sql,con,tx); cmd.Parameters.AddWithValue("token",token); await using var r=await cmd.ExecuteReaderAsync();
            if(!await r.ReadAsync()){await tx.RollbackAsync(); return Results.NotFound();}
            var vehicleId=r.GetGuid(0); var companyId=r.GetGuid(1); var current=r.GetInt32(2); var threshold=r.GetInt32(3); await r.CloseAsync();
            if(req.Mileage < current){await tx.RollbackAsync(); return Results.UnprocessableEntity(new{success=false,error=new{code="MILEAGE_LOWER_THAN_CURRENT",message=$"El kilometraje debe ser igual o mayor que {current:N0} km.",details=new{currentMileage=current}}});}
            var diff=req.Mileage-current; if(diff>=threshold && !req.ExceptionConfirmed){await tx.RollbackAsync(); return Results.Ok(new{success=true,data=new{status="CONFIRMATION_REQUIRED",previousMileage=current,newMileage=req.Mileage,difference=diff,threshold}});}
            var exceptional=diff>=threshold;
            await using(var c1=new NpgsqlCommand("INSERT INTO mileage_readings(vehicle_id,mileage,source,is_exceptional,exception_confirmed) VALUES(@v,@km,'PUBLIC_QR',@e,@e)",con,tx)){c1.Parameters.AddWithValue("v",vehicleId);c1.Parameters.AddWithValue("km",req.Mileage);c1.Parameters.AddWithValue("e",exceptional);await c1.ExecuteNonQueryAsync();}
            await using(var c2=new NpgsqlCommand("UPDATE vehicles SET current_mileage=@km,mileage_updated_at=now(),updated_at=now() WHERE id=@v",con,tx)){c2.Parameters.AddWithValue("km",req.Mileage);c2.Parameters.AddWithValue("v",vehicleId);await c2.ExecuteNonQueryAsync();}
            await using(var c3=new NpgsqlCommand("INSERT INTO audit_logs(company_id,entity_type,entity_id,action,old_values,new_values,source) VALUES(@c,'VEHICLE',@v,'MILEAGE_UPDATE',jsonb_build_object('mileage',@old),jsonb_build_object('mileage',@new),'PUBLIC_QR')",con,tx)){c3.Parameters.AddWithValue("c",companyId);c3.Parameters.AddWithValue("v",vehicleId);c3.Parameters.AddWithValue("old",current);c3.Parameters.AddWithValue("new",req.Mileage);await c3.ExecuteNonQueryAsync();}
            await tx.CommitAsync(); return Results.Ok(new{success=true,data=new{status="UPDATED",previousMileage=current,newMileage=req.Mileage,difference=diff,exceptional}});
        }).RequireRateLimiting("public");
    }
}
