using System.Security.Claims;
using Npgsql;
using static Helpers;

public static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this WebApplication app, string connectionString)
    {
        app.MapPost("/api/v1/vehicles/{vehicleId:guid}/baselines", async (ClaimsPrincipal principal, Guid vehicleId, BaselineRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(req.LastServiceMileage.HasValue && req.LastServiceMileage.Value<0)
                return Results.BadRequest(new{success=false,error=new{message="El kilometraje no puede ser negativo."}});
            var companyId=CompanyId(principal);var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var verify=@"SELECT s.company_service_id FROM vehicles v JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id WHERE v.id=@v AND v.company_id=@c AND s.id=@s";
            Guid? companyServiceId=null;
            await using(var c=new NpgsqlCommand(verify,con)){c.Parameters.AddWithValue("v",vehicleId);c.Parameters.AddWithValue("c",companyId);c.Parameters.AddWithValue("s",req.PlanServiceId);var x=await c.ExecuteScalarAsync();if(x is null)return Results.BadRequest(new{success=false,error=new{message="El servicio no pertenece al plan activo del vehículo."}});if(x is Guid g)companyServiceId=g;}
            var sql=@"INSERT INTO vehicle_service_baselines(vehicle_id,plan_service_id,company_service_id,last_service_mileage,last_service_date,source,created_by_user_id)
                      VALUES(@v,@s,@cs,@km,@d,'ADMIN',@u)
                      ON CONFLICT(vehicle_id,plan_service_id) DO UPDATE SET company_service_id=excluded.company_service_id,last_service_mileage=excluded.last_service_mileage,last_service_date=excluded.last_service_date,created_by_user_id=excluded.created_by_user_id,created_at=now()";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("s",req.PlanServiceId);cmd.Parameters.AddWithValue("cs",(object?)companyServiceId??DBNull.Value);cmd.Parameters.AddWithValue("km",(object?)req.LastServiceMileage??DBNull.Value);cmd.Parameters.AddWithValue("d",(object?)req.LastServiceDate?.Date??DBNull.Value);cmd.Parameters.AddWithValue("u",userId);await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapGet("/api/v1/vehicles/{vehicleId:guid}/maintenance-status", async (ClaimsPrincipal principal, Guid vehicleId) =>
        {
            if(!CanAccessVehicle(principal,vehicleId))return Results.Forbid();
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            int currentMileage; string plate; string? internalNumber; bool individualServices=false;
            await using(var vc=new NpgsqlCommand(@"SELECT v.current_mileage,v.plate,COALESCE(mp.is_vehicle_specific,false),v.internal_number
                FROM vehicles v
                LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
                LEFT JOIN maintenance_plan_versions pv ON pv.id=a.plan_version_id
                LEFT JOIN maintenance_plans mp ON mp.id=pv.maintenance_plan_id
                WHERE v.id=@v AND v.company_id=@c",con)){vc.Parameters.AddWithValue("v",vehicleId);vc.Parameters.AddWithValue("c",companyId);await using var vr=await vc.ExecuteReaderAsync();if(!await vr.ReadAsync())return Results.NotFound();currentMileage=vr.GetInt32(0);plate=vr.GetString(1);individualServices=vr.GetBoolean(2);internalNumber=vr.IsDBNull(3)?null:vr.GetString(3);}
            // El baseline se busca primero por coincidencia exacta de plan_service_id (comportamiento clásico) y,
            // si no hay, por company_service_id — así el "último servicio" no se pierde al reasignar el vehículo
            // a otro plan que reutiliza el mismo servicio del catálogo.
            var sql=@"SELECT s.id,s.name,s.category,s.specification,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days,bl.last_service_mileage,bl.last_service_date
                      FROM vehicle_plan_assignments a
                      JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id AND s.active=true
                      LEFT JOIN LATERAL (
                        SELECT b.last_service_mileage,b.last_service_date FROM vehicle_service_baselines b
                        WHERE b.vehicle_id=a.vehicle_id AND (b.plan_service_id=s.id OR (s.company_service_id IS NOT NULL AND b.company_service_id=s.company_service_id))
                        ORDER BY (b.plan_service_id=s.id) DESC, b.created_at DESC LIMIT 1
                      ) bl ON true
                      WHERE a.vehicle_id=@v AND a.active=true ORDER BY s.sort_order,s.name";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);await using var r=await cmd.ExecuteReaderAsync();
            var items=new List<MaintenanceStatusItem>();
            while(await r.ReadAsync())
            {
                var serviceId=r.GetGuid(0);var name=r.GetString(1);var intervalKm=r.IsDBNull(4)?(int?)null:r.GetInt32(4);var intervalMonths=r.IsDBNull(5)?(int?)null:r.GetInt32(5);var prealertKm=r.IsDBNull(6)?0:r.GetInt32(6);var prealertDays=r.IsDBNull(7)?0:r.GetInt32(7);var lastKm=r.IsDBNull(8)?(int?)null:r.GetInt32(8);var lastDate=r.IsDBNull(9)?(DateTime?)null:r.GetDateTime(9);
                items.Add(CalcStatus(serviceId,name,currentMileage,intervalKm,intervalMonths,prealertKm,prealertDays,lastKm,lastDate));
            }
            if(items.Count==0) return Results.Ok(new{success=true,data=new{plate,internalNumber,currentMileage,overallStatus="NO_PLAN",hasIncompleteHistory=false,individualServices,services=items}});
            var overall=items.Any(x=>x.Status=="OVERDUE")?"OVERDUE":items.Any(x=>x.Status=="DUE_SOON")?"DUE_SOON":items.All(x=>x.Status=="NO_BASELINE")?"NO_BASELINE":"UP_TO_DATE";
            var incomplete=items.Any(x=>x.Status=="NO_BASELINE");
            return Results.Ok(new{success=true,data=new{plate,internalNumber,currentMileage,overallStatus=overall,hasIncompleteHistory=incomplete,individualServices,services=items}});
        }).RequireAuthorization();

        // CENTRO DE CONTROL + PREDICCIÓN V8
        app.MapGet("/api/v1/dashboard", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString); await con.OpenAsync();

            var sql=@"SELECT v.id,v.plate,v.internal_number,v.brand,v.model,v.variant,v.current_mileage,
                             s.id,s.name,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days,
                             bl.last_service_mileage,bl.last_service_date
                      FROM vehicles v
                      LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
                      LEFT JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id AND s.active=true
                      LEFT JOIN LATERAL (
                        SELECT b.last_service_mileage,b.last_service_date FROM vehicle_service_baselines b
                        WHERE s.id IS NOT NULL AND b.vehicle_id=v.id AND (b.plan_service_id=s.id OR (s.company_service_id IS NOT NULL AND b.company_service_id=s.company_service_id))
                        ORDER BY (b.plan_service_id=s.id) DESC, b.created_at DESC LIMIT 1
                      ) bl ON true
                      WHERE v.company_id=@c AND v.status='ACTIVE'
                      ORDER BY v.plate,s.sort_order,s.name";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("c",companyId);
            await using var r=await cmd.ExecuteReaderAsync();

            var vehicles=new Dictionary<Guid,DashboardVehicle>();
            while(await r.ReadAsync())
            {
                var id=r.GetGuid(0);
                if(!vehicles.TryGetValue(id,out var dv))
                {
                    dv=new DashboardVehicle(id,r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetInt32(6),new List<MaintenanceStatusItem>());
                    vehicles[id]=dv;
                }
                if(!r.IsDBNull(7))
                {
                    var serviceId=r.GetGuid(7);var name=r.GetString(8);var intervalKm=r.IsDBNull(9)?(int?)null:r.GetInt32(9);var intervalMonths=r.IsDBNull(10)?(int?)null:r.GetInt32(10);var prealertKm=r.IsDBNull(11)?0:r.GetInt32(11);var prealertDays=r.IsDBNull(12)?0:r.GetInt32(12);var lastKm=r.IsDBNull(13)?(int?)null:r.GetInt32(13);var lastDate=r.IsDBNull(14)?(DateTime?)null:r.GetDateTime(14);
                    dv.Services.Add(CalcStatus(serviceId,name,dv.CurrentMileage,intervalKm,intervalMonths,prealertKm,prealertDays,lastKm,lastDate));
                }
            }

            // Predicción V9: promedio diario + estabilidad de uso.
            // Requiere al menos 3 días distintos. La confianza depende de la cantidad
            // de días y de cuánto varía el kilometraje diario entre intervalos.
            var predictions=new Dictionary<Guid,VehiclePrediction>();
            await r.DisposeAsync();
            foreach(var v in vehicles.Values)
            {
                await using var pcmd=new NpgsqlCommand(@"SELECT mileage,created_at FROM mileage_readings
                    WHERE vehicle_id=@v AND created_at >= now()-interval '30 days'
                    ORDER BY created_at",con);
                pcmd.Parameters.AddWithValue("v",v.Id);
                await using var pr=await pcmd.ExecuteReaderAsync();
                var daily=new SortedDictionary<DateTime,int>();
                while(await pr.ReadAsync())
                {
                    var day=pr.GetDateTime(1).Date; var km=pr.GetInt32(0);
                    if(!daily.ContainsKey(day) || km>daily[day]) daily[day]=km;
                }
                if(daily.Count>=3)
                {
                    var vals=daily.ToArray();
                    var rates=new List<double>();
                    for(var i=1;i<vals.Length;i++)
                    {
                        var days=(vals[i].Key-vals[i-1].Key).TotalDays;
                        var delta=vals[i].Value-vals[i-1].Value;
                        if(days>0 && delta>=0) rates.Add(delta/days);
                    }
                    if(rates.Count>=2)
                    {
                        var avg=rates.Average();
                        if(avg>0 && avg<2000)
                        {
                            var variance=rates.Sum(x=>(x-avg)*(x-avg))/rates.Count;
                            var stdev=Math.Sqrt(variance);
                            var cv=avg>0?stdev/avg:1.0;
                            var confidence=(daily.Count>=7 && cv<=0.25)?"HIGH":(daily.Count>=5 && cv<=0.50)?"MEDIUM":"LOW";
                            predictions[v.Id]=new VehiclePrediction(Math.Round(avg,1),daily.Count,Math.Round(cv,2),confidence);
                        }
                    }
                }
            }

            var rows=new List<DashboardPriority>(); int up=0,due=0,over=0,noHistory=0,noPlan=0;
            foreach(var v in vehicles.Values)
            {
                string overall;
                if(v.Services.Count==0){overall="NO_PLAN";noPlan++;}
                else if(v.Services.Any(x=>x.Status=="OVERDUE")){overall="OVERDUE";over++;}
                else if(v.Services.Any(x=>x.Status=="DUE_SOON")){overall="DUE_SOON";due++;}
                else if(v.Services.Any(x=>x.Status=="NO_BASELINE")){overall="NO_BASELINE";noHistory++;}
                else {overall="UP_TO_DATE";up++;}

                foreach(var x in v.Services)
                    {
                        double? avg=null; int? estimatedDays=null; DateTime? estimatedDate=null; int samples=0;
                        if(predictions.TryGetValue(v.Id,out var pred))
                        {
                            avg=pred.AverageKmPerDay;samples=pred.SampleDays;
                            if(x.RemainingKm.HasValue && x.RemainingKm.Value>0)
                            {
                                estimatedDays=(int)Math.Ceiling(x.RemainingKm.Value/pred.AverageKmPerDay);
                                estimatedDate=DateTime.UtcNow.Date.AddDays(estimatedDays.Value);
                            }
                        }
                        rows.Add(new DashboardPriority(v.Id,v.Plate,v.InternalNumber,v.Brand,v.Model,v.Variant,v.CurrentMileage,x.ServiceId,x.Name,x.Status,x.NextDueMileage,x.RemainingKm,x.NextDueDate,avg,estimatedDays,estimatedDate,samples));
                    }
            }
            var priority=rows.OrderBy(x=>x.Status=="OVERDUE"?0:x.Status=="DUE_SOON"?1:x.Status=="NO_BASELINE"?2:3)
                .ThenBy(x=>x.Status=="DUE_SOON" && x.EstimatedDays.HasValue?x.EstimatedDays.Value:int.MaxValue)
                .ThenBy(x=>x.RemainingKm.HasValue?Math.Abs(x.RemainingKm.Value):int.MaxValue).ToList();
            var todayUpdates=new List<object>(); long todayKm=0;
            await using(var mcmd=new NpgsqlCommand(@"SELECT v.id,v.plate,v.internal_number,m.mileage,m.source,m.created_at
              FROM mileage_readings m JOIN vehicles v ON v.id=m.vehicle_id
              WHERE v.company_id=@c AND (m.created_at AT TIME ZONE 'America/Bogota')::date=(now() AT TIME ZONE 'America/Bogota')::date
              ORDER BY m.created_at DESC LIMIT 12",con))
            {
                mcmd.Parameters.AddWithValue("c",companyId);await using var mr=await mcmd.ExecuteReaderAsync();
                while(await mr.ReadAsync())todayUpdates.Add(new{vehicleId=mr.GetGuid(0),plate=mr.GetString(1),internalNumber=mr.IsDBNull(2)?null:mr.GetString(2),mileage=mr.GetInt32(3),source=mr.GetString(4),createdAt=mr.GetDateTime(5)});
            }
            await using(var kcmd=new NpgsqlCommand(@"SELECT COALESCE(sum(greatest(0,mileage-lag_mileage)),0) FROM (
              SELECT m.mileage,lag(m.mileage) over(partition by m.vehicle_id order by m.created_at) lag_mileage
              FROM mileage_readings m JOIN vehicles v ON v.id=m.vehicle_id
              WHERE v.company_id=@c AND (m.created_at AT TIME ZONE 'America/Bogota')::date=(now() AT TIME ZONE 'America/Bogota')::date) q WHERE lag_mileage IS NOT NULL",con))
            {kcmd.Parameters.AddWithValue("c",companyId);todayKm=Convert.ToInt64(await kcmd.ExecuteScalarAsync());}
            return Results.Ok(new{success=true,data=new{total=vehicles.Count,upToDate=up,dueSoon=due,overdue=over,noHistory,noPlan,priorities=priority,todayUpdates,todayKm}});
        }).RequireAuthorization();

        // REGISTRO DE MANTENIMIENTO
        app.MapPost("/api/v1/vehicles/{vehicleId:guid}/maintenance", async (ClaimsPrincipal principal, Guid vehicleId, RegisterMaintenanceRequest req) =>
        {
            if(!CanAccessVehicle(principal,vehicleId))return Results.Forbid();
            var companyId=CompanyId(principal); var userId=UserId(principal);
            if(req.ServiceIds is null || req.ServiceIds.Count==0) return Results.BadRequest(new{success=false,error=new{code="NO_SERVICES",message="Selecciona al menos un servicio."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            try
            {
                int currentMileage; int threshold;
                await using(var vc=new NpgsqlCommand("SELECT v.current_mileage,c.exceptional_mileage_threshold FROM vehicles v JOIN companies c ON c.id=v.company_id WHERE v.id=@v AND v.company_id=@c FOR UPDATE OF v",con,tx))
                {vc.Parameters.AddWithValue("v",vehicleId);vc.Parameters.AddWithValue("c",companyId);await using var vr=await vc.ExecuteReaderAsync();if(!await vr.ReadAsync()){await tx.RollbackAsync();return Results.NotFound();}currentMileage=vr.GetInt32(0);threshold=vr.GetInt32(1);}
                if(req.Mileage<currentMileage){await tx.RollbackAsync();return Results.UnprocessableEntity(new{success=false,error=new{code="MILEAGE_LOWER_THAN_CURRENT",message=$"El mantenimiento no puede registrarse por debajo del kilometraje actual ({currentMileage:N0} km)."}});}
                var jump=req.Mileage-currentMileage;
                if(jump>=threshold && !req.ExceptionConfirmed)
                {
                    await tx.RollbackAsync();
                    return Results.Ok(new{success=true,data=new{status="CONFIRMATION_REQUIRED",previousMileage=currentMileage,newMileage=req.Mileage,difference=jump,threshold}});
                }
                var recordId=Guid.NewGuid();
                await using(var h=new NpgsqlCommand("INSERT INTO maintenance_records(id,company_id,vehicle_id,technician_user_id,service_date,mileage,notes) VALUES(@id,@c,@v,@u,@d,@km,@n)",con,tx))
                {h.Parameters.AddWithValue("id",recordId);h.Parameters.AddWithValue("c",companyId);h.Parameters.AddWithValue("v",vehicleId);h.Parameters.AddWithValue("u",userId);h.Parameters.AddWithValue("d",req.ServiceDate.Date);h.Parameters.AddWithValue("km",req.Mileage);h.Parameters.AddWithValue("n",(object?)req.Notes??DBNull.Value);await h.ExecuteNonQueryAsync();}
                foreach(var serviceId in req.ServiceIds.Distinct())
                {
                    string name; string? spec; int? intervalKm; int? intervalMonths; Guid? companyServiceId;
                    await using(var sc=new NpgsqlCommand(@"SELECT s.name,s.specification,s.interval_km,s.interval_months,s.company_service_id FROM maintenance_plan_services s JOIN vehicle_plan_assignments a ON a.plan_version_id=s.plan_version_id AND a.active=true WHERE a.vehicle_id=@v AND s.id=@s AND s.active=true",con,tx))
                    {sc.Parameters.AddWithValue("v",vehicleId);sc.Parameters.AddWithValue("s",serviceId);await using var r=await sc.ExecuteReaderAsync();if(!await r.ReadAsync()){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{code="INVALID_SERVICE",message="Uno de los servicios no pertenece al plan activo del vehículo."}});}name=r.GetString(0);spec=r.IsDBNull(1)?null:r.GetString(1);intervalKm=r.IsDBNull(2)?null:r.GetInt32(2);intervalMonths=r.IsDBNull(3)?null:r.GetInt32(3);companyServiceId=r.IsDBNull(4)?(Guid?)null:r.GetGuid(4);}
                    int? nextKm=intervalKm.HasValue?req.Mileage+intervalKm.Value:null; DateTime? nextDate=intervalMonths.HasValue?req.ServiceDate.Date.AddMonths(intervalMonths.Value):null;
                    await using(var it=new NpgsqlCommand(@"INSERT INTO maintenance_record_items(maintenance_record_id,plan_service_id,company_service_id,service_name_snapshot,specification_snapshot,interval_km_snapshot,interval_months_snapshot,next_due_mileage,next_due_date) VALUES(@r,@s,@cs,@n,@sp,@ik,@im,@nk,@nd)",con,tx))
                    {it.Parameters.AddWithValue("r",recordId);it.Parameters.AddWithValue("s",serviceId);it.Parameters.AddWithValue("cs",(object?)companyServiceId??DBNull.Value);it.Parameters.AddWithValue("n",name);it.Parameters.AddWithValue("sp",(object?)spec??DBNull.Value);it.Parameters.AddWithValue("ik",(object?)intervalKm??DBNull.Value);it.Parameters.AddWithValue("im",(object?)intervalMonths??DBNull.Value);it.Parameters.AddWithValue("nk",(object?)nextKm??DBNull.Value);it.Parameters.AddWithValue("nd",(object?)nextDate?.Date??DBNull.Value);await it.ExecuteNonQueryAsync();}
                    await using(var b=new NpgsqlCommand(@"INSERT INTO vehicle_service_baselines(vehicle_id,plan_service_id,company_service_id,last_service_mileage,last_service_date,source,created_by_user_id) VALUES(@v,@s,@cs,@km,@d,'MAINTENANCE',@u) ON CONFLICT(vehicle_id,plan_service_id) DO UPDATE SET company_service_id=excluded.company_service_id,last_service_mileage=excluded.last_service_mileage,last_service_date=excluded.last_service_date,source='MAINTENANCE',created_by_user_id=excluded.created_by_user_id,created_at=now()",con,tx))
                    {b.Parameters.AddWithValue("v",vehicleId);b.Parameters.AddWithValue("s",serviceId);b.Parameters.AddWithValue("cs",(object?)companyServiceId??DBNull.Value);b.Parameters.AddWithValue("km",req.Mileage);b.Parameters.AddWithValue("d",req.ServiceDate.Date);b.Parameters.AddWithValue("u",userId);await b.ExecuteNonQueryAsync();}
                }
                if(req.Mileage>currentMileage)
                {
                    await using(var mr=new NpgsqlCommand("INSERT INTO mileage_readings(vehicle_id,mileage,source,created_by_user_id) VALUES(@v,@km,'TECHNICIAN',@u)",con,tx)){mr.Parameters.AddWithValue("v",vehicleId);mr.Parameters.AddWithValue("km",req.Mileage);mr.Parameters.AddWithValue("u",userId);await mr.ExecuteNonQueryAsync();}
                    await using(var uv=new NpgsqlCommand("UPDATE vehicles SET current_mileage=@km,mileage_updated_at=now(),updated_at=now() WHERE id=@v",con,tx)){uv.Parameters.AddWithValue("km",req.Mileage);uv.Parameters.AddWithValue("v",vehicleId);await uv.ExecuteNonQueryAsync();}
                }
                await using(var au=new NpgsqlCommand("INSERT INTO audit_logs(company_id,user_id,entity_type,entity_id,action,new_values,source) VALUES(@c,@u,'MAINTENANCE_RECORD',@r,'REGISTER_MAINTENANCE',jsonb_build_object('vehicleId',@v,'mileage',@km),'WEB')",con,tx)){au.Parameters.AddWithValue("c",companyId);au.Parameters.AddWithValue("u",userId);au.Parameters.AddWithValue("r",recordId);au.Parameters.AddWithValue("v",vehicleId);au.Parameters.AddWithValue("km",req.Mileage);await au.ExecuteNonQueryAsync();}
                await tx.CommitAsync();return Results.Ok(new{success=true,data=new{id=recordId,mileage=req.Mileage,serviceDate=req.ServiceDate.Date}});
            }
            catch(Exception ex){await tx.RollbackAsync();app.Logger.LogError(ex,"Register maintenance failed");return Results.Json(new{success=false,error=new{code="MAINTENANCE_SAVE_FAILED",message="No fue posible registrar el mantenimiento."}},statusCode:500);}
        }).RequireAuthorization();

        app.MapGet("/api/v1/vehicles/{vehicleId:guid}/maintenance-history", async (ClaimsPrincipal principal, Guid vehicleId) =>
        {
            if(!CanAccessVehicle(principal,vehicleId))return Results.Forbid();
            var companyId=CompanyId(principal);await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT mr.id,mr.service_date,mr.mileage,mr.notes,u.full_name,mri.service_name_snapshot,mri.next_due_mileage,mri.next_due_date FROM maintenance_records mr JOIN maintenance_record_items mri ON mri.maintenance_record_id=mr.id LEFT JOIN users u ON u.id=mr.technician_user_id WHERE mr.vehicle_id=@v AND mr.company_id=@c ORDER BY mr.service_date DESC,mr.created_at DESC,mri.service_name_snapshot";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",companyId);await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),serviceDate=r.GetDateTime(1),mileage=r.GetInt32(2),notes=r.IsDBNull(3)?null:r.GetString(3),technician=r.IsDBNull(4)?null:r.GetString(4),serviceName=r.GetString(5),nextDueMileage=r.IsDBNull(6)?(int?)null:r.GetInt32(6),nextDueDate=r.IsDBNull(7)?(DateTime?)null:r.GetDateTime(7)});return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        // HISTORIAL DE KILOMETRAJE / CARGA HISTÓRICA ADMINISTRATIVA V8.1
        app.MapGet("/api/v1/vehicles/{vehicleId:guid}/mileage-history", async (ClaimsPrincipal principal, Guid vehicleId) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using(var check=new NpgsqlCommand("SELECT 1 FROM vehicles WHERE id=@v AND company_id=@c",con))
            {check.Parameters.AddWithValue("v",vehicleId);check.Parameters.AddWithValue("c",companyId);if(await check.ExecuteScalarAsync() is null)return Results.NotFound();}
            var sql=@"SELECT id,mileage,source,is_exceptional,created_at
                      FROM mileage_readings WHERE vehicle_id=@v
                      ORDER BY created_at DESC,id DESC LIMIT 200";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);
            await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync()) list.Add(new{id=r.GetGuid(0),mileage=r.GetInt32(1),source=r.GetString(2),isExceptional=r.GetBoolean(3),createdAt=r.GetDateTime(4)});
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPost("/api/v1/vehicles/{vehicleId:guid}/mileage-history", async (ClaimsPrincipal principal, Guid vehicleId, HistoricalMileageRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);var userId=UserId(principal);
            if(req.Mileage<0)return Results.BadRequest(new{success=false,error=new{message="El kilometraje no puede ser negativo."}});
            var readingAt=req.ReadingDate.Date.AddHours(12);
            if(readingAt>DateTime.UtcNow.AddDays(1))return Results.BadRequest(new{success=false,error=new{message="La fecha de la lectura no puede estar en el futuro."}});

            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            try
            {
                await using(var check=new NpgsqlCommand("SELECT 1 FROM vehicles WHERE id=@v AND company_id=@c FOR UPDATE",con,tx))
                {check.Parameters.AddWithValue("v",vehicleId);check.Parameters.AddWithValue("c",companyId);if(await check.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.NotFound();}}

                int? previousKm=null,nextKm=null;
                await using(var prev=new NpgsqlCommand("SELECT mileage FROM mileage_readings WHERE vehicle_id=@v AND created_at<@d ORDER BY created_at DESC LIMIT 1",con,tx))
                {prev.Parameters.AddWithValue("v",vehicleId);prev.Parameters.AddWithValue("d",readingAt);var x=await prev.ExecuteScalarAsync();if(x is int k)previousKm=k;}
                await using(var next=new NpgsqlCommand("SELECT mileage FROM mileage_readings WHERE vehicle_id=@v AND created_at>@d ORDER BY created_at ASC LIMIT 1",con,tx))
                {next.Parameters.AddWithValue("v",vehicleId);next.Parameters.AddWithValue("d",readingAt);var x=await next.ExecuteScalarAsync();if(x is int k)nextKm=k;}
                if(previousKm.HasValue && req.Mileage<previousKm.Value){await tx.RollbackAsync();return Results.UnprocessableEntity(new{success=false,error=new{message=$"Para esa fecha, el kilometraje no puede ser menor que la lectura anterior ({previousKm.Value:N0} km)."}});}
                if(nextKm.HasValue && req.Mileage>nextKm.Value){await tx.RollbackAsync();return Results.UnprocessableEntity(new{success=false,error=new{message=$"Para esa fecha, el kilometraje no puede superar la lectura posterior ({nextKm.Value:N0} km)."}});}

                await using(var ins=new NpgsqlCommand("INSERT INTO mileage_readings(vehicle_id,mileage,source,created_by_user_id,created_at) VALUES(@v,@km,'ADMIN_HISTORICAL',@u,@d)",con,tx))
                {ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("km",req.Mileage);ins.Parameters.AddWithValue("u",userId);ins.Parameters.AddWithValue("d",readingAt);await ins.ExecuteNonQueryAsync();}

                // Si es la lectura cronológicamente más reciente, también actualiza el odómetro maestro.
                await using(var latest=new NpgsqlCommand("SELECT mileage,created_at FROM mileage_readings WHERE vehicle_id=@v ORDER BY created_at DESC,id DESC LIMIT 1",con,tx))
                {latest.Parameters.AddWithValue("v",vehicleId);await using var lr=await latest.ExecuteReaderAsync();if(await lr.ReadAsync()){var latestKm=lr.GetInt32(0);var latestAt=lr.GetDateTime(1);await lr.CloseAsync();await using var uv=new NpgsqlCommand("UPDATE vehicles SET current_mileage=@km,mileage_updated_at=@d,updated_at=now() WHERE id=@v",con,tx);uv.Parameters.AddWithValue("km",latestKm);uv.Parameters.AddWithValue("d",latestAt);uv.Parameters.AddWithValue("v",vehicleId);await uv.ExecuteNonQueryAsync();}}

                await using(var au=new NpgsqlCommand("INSERT INTO audit_logs(company_id,user_id,entity_type,entity_id,action,new_values,source) VALUES(@c,@u,'VEHICLE',@v,'ADMIN_HISTORICAL_MILEAGE',jsonb_build_object('mileage',@km,'date',@d),'WEB')",con,tx))
                {au.Parameters.AddWithValue("c",companyId);au.Parameters.AddWithValue("u",userId);au.Parameters.AddWithValue("v",vehicleId);au.Parameters.AddWithValue("km",req.Mileage);au.Parameters.AddWithValue("d",readingAt);await au.ExecuteNonQueryAsync();}
                await tx.CommitAsync();return Results.Ok(new{success=true,data=new{mileage=req.Mileage,createdAt=readingAt}});
            }
            catch(Exception ex){await tx.RollbackAsync();app.Logger.LogError(ex,"Historical mileage save failed");return Results.Json(new{success=false,error=new{message="No fue posible guardar la lectura histórica."}},statusCode:500);}
        }).RequireAuthorization();

        // CORRECCIÓN DIRECTA DE KILOMETRAJE ACTUAL — solo administrador, sin restricciones de umbral ni de "no puede bajar"
        app.MapPatch("/api/v1/vehicles/{vehicleId:guid}/mileage", async (ClaimsPrincipal principal, Guid vehicleId, CorrectMileageRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);var userId=UserId(principal);
            if(req.Mileage<0)return Results.BadRequest(new{success=false,error=new{message="El kilometraje no puede ser negativo."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            try
            {
                int previousMileage;
                await using(var check=new NpgsqlCommand("SELECT current_mileage FROM vehicles WHERE id=@v AND company_id=@c FOR UPDATE",con,tx))
                {check.Parameters.AddWithValue("v",vehicleId);check.Parameters.AddWithValue("c",companyId);var x=await check.ExecuteScalarAsync();if(x is null){await tx.RollbackAsync();return Results.NotFound();}previousMileage=(int)x;}
                var now=DateTime.UtcNow;
                await using(var ins=new NpgsqlCommand("INSERT INTO mileage_readings(vehicle_id,mileage,source,created_by_user_id,created_at) VALUES(@v,@km,'ADMIN_CORRECTION',@u,@d)",con,tx))
                {ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("km",req.Mileage);ins.Parameters.AddWithValue("u",userId);ins.Parameters.AddWithValue("d",now);await ins.ExecuteNonQueryAsync();}
                await using(var uv=new NpgsqlCommand("UPDATE vehicles SET current_mileage=@km,mileage_updated_at=@d,updated_at=now() WHERE id=@v",con,tx))
                {uv.Parameters.AddWithValue("km",req.Mileage);uv.Parameters.AddWithValue("d",now);uv.Parameters.AddWithValue("v",vehicleId);await uv.ExecuteNonQueryAsync();}
                await using(var au=new NpgsqlCommand("INSERT INTO audit_logs(company_id,user_id,entity_type,entity_id,action,old_values,new_values,source) VALUES(@c,@u,'VEHICLE',@v,'ADMIN_MILEAGE_CORRECTION',jsonb_build_object('mileage',@old),jsonb_build_object('mileage',@new),'WEB')",con,tx))
                {au.Parameters.AddWithValue("c",companyId);au.Parameters.AddWithValue("u",userId);au.Parameters.AddWithValue("v",vehicleId);au.Parameters.AddWithValue("old",previousMileage);au.Parameters.AddWithValue("new",req.Mileage);await au.ExecuteNonQueryAsync();}
                await tx.CommitAsync();return Results.Ok(new{success=true,data=new{previousMileage,mileage=req.Mileage}});
            }
            catch(Exception ex){await tx.RollbackAsync();app.Logger.LogError(ex,"Mileage correction failed");return Results.Json(new{success=false,error=new{message="No fue posible corregir el kilometraje."}},statusCode:500);}
        }).RequireAuthorization();
    }
}
