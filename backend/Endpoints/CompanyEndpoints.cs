using System.Security.Claims;
using Npgsql;
using static Helpers;

public static class CompanyEndpoints
{
    public static void MapCompanyEndpoints(this WebApplication app, string connectionString)
    {
        // MULTIEMPRESA V12
        app.MapGet("/api/v1/company", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand(@"SELECT id,name,COALESCE(code,''),status,legal_name,tax_id,phone,email,address,logo_data_url,
         notification_enabled,notification_internal,notification_email_enabled,notification_email,notification_due_soon,notification_overdue,notification_repeat_days,exceptional_mileage_threshold
         FROM companies WHERE id=@c",con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
            await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound();
            return Results.Ok(new{success=true,data=new{id=r.GetGuid(0),name=r.GetString(1),code=r.GetString(2),status=r.GetString(3),legalName=r.IsDBNull(4)?null:r.GetString(4),taxId=r.IsDBNull(5)?null:r.GetString(5),phone=r.IsDBNull(6)?null:r.GetString(6),email=r.IsDBNull(7)?null:r.GetString(7),address=r.IsDBNull(8)?null:r.GetString(8),logoDataUrl=r.IsDBNull(9)?null:r.GetString(9),notificationEnabled=r.GetBoolean(10),notificationInternal=r.GetBoolean(11),notificationEmailEnabled=r.GetBoolean(12),notificationEmail=r.IsDBNull(13)?null:r.GetString(13),notificationDueSoon=r.GetBoolean(14),notificationOverdue=r.GetBoolean(15),notificationRepeatDays=r.GetInt32(16),exceptionalMileageThreshold=r.GetInt32(17)}});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/company", async (ClaimsPrincipal principal, UpdateCompanyRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre de la empresa es obligatorio."}});
            if(!string.IsNullOrEmpty(req.LogoDataUrl) && (req.LogoDataUrl.Length>2_000_000 || !System.Text.RegularExpressions.Regex.IsMatch(req.LogoDataUrl,@"^data:image/(png|jpeg|jpg|webp|gif);base64,[A-Za-z0-9+/]+={0,2}$")))
                return Results.BadRequest(new{success=false,error=new{message="El logo debe ser una imagen válida (PNG, JPG, WEBP o GIF) de tamaño razonable."}});
            if(req.ExceptionalMileageThreshold<=0)
                return Results.BadRequest(new{success=false,error=new{message="El umbral de kilometraje excepcional debe ser mayor que cero."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand(@"UPDATE companies SET name=@n,legal_name=@l,tax_id=@t,phone=@p,email=@e,address=@a,logo_data_url=@logo,exceptional_mileage_threshold=@emt WHERE id=@c",con);
            cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("l",(object?)req.LegalName?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("t",(object?)req.TaxId?.Trim()??DBNull.Value);
            cmd.Parameters.AddWithValue("p",(object?)req.Phone?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("e",(object?)req.Email?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("a",(object?)req.Address?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("logo",(object?)req.LogoDataUrl??DBNull.Value);cmd.Parameters.AddWithValue("emt",req.ExceptionalMileageThreshold);cmd.Parameters.AddWithValue("c",CompanyId(principal));
            await cmd.ExecuteNonQueryAsync();return Results.Ok(new{success=true});
         }).RequireAuthorization();

        app.MapPut("/api/v1/company/notifications", async (ClaimsPrincipal principal, NotificationSettingsRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(req.RepeatDays is < 1 or > 90)return Results.BadRequest(new{success=false,error=new{message="La frecuencia debe estar entre 1 y 90 días."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand(@"UPDATE companies SET notification_enabled=@en,notification_internal=@ni,notification_email_enabled=@ee,notification_email=@em,notification_due_soon=@ds,notification_overdue=@ov,notification_repeat_days=@rd WHERE id=@c",con);
            cmd.Parameters.AddWithValue("en",req.Enabled);cmd.Parameters.AddWithValue("ni",req.Internal);cmd.Parameters.AddWithValue("ee",req.EmailEnabled);cmd.Parameters.AddWithValue("em",(object?)req.Email?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("ds",req.DueSoon);cmd.Parameters.AddWithValue("ov",req.Overdue);cmd.Parameters.AddWithValue("rd",req.RepeatDays);cmd.Parameters.AddWithValue("c",CompanyId(principal));await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapGet("/api/v1/notifications", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT v.id,v.plate,v.internal_number,v.current_mileage,s.id,s.name,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days,b.last_service_mileage,b.last_service_date
              FROM vehicles v JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
              JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id AND s.active=true
              LEFT JOIN vehicle_service_baselines b ON b.vehicle_id=v.id AND b.plan_service_id=s.id
              WHERE v.company_id=@c AND v.status='ACTIVE'";
            var candidates=new List<(Guid VehicleId,string Plate,string? InternalNumber,int CurrentMileage,MaintenanceStatusItem Item)>();
            await using(var cmd=new NpgsqlCommand(sql,con))
            {
                cmd.Parameters.AddWithValue("c",companyId);await using var r=await cmd.ExecuteReaderAsync();
                while(await r.ReadAsync())
                {
                    var currentMileage=r.GetInt32(3);
                    var serviceId=r.GetGuid(4);var name=r.GetString(5);var intervalKm=r.IsDBNull(6)?(int?)null:r.GetInt32(6);var intervalMonths=r.IsDBNull(7)?(int?)null:r.GetInt32(7);var prealertKm=r.IsDBNull(8)?0:r.GetInt32(8);var prealertDays=r.IsDBNull(9)?0:r.GetInt32(9);var lastKm=r.IsDBNull(10)?(int?)null:r.GetInt32(10);var lastDate=r.IsDBNull(11)?(DateTime?)null:r.GetDateTime(11);
                    var item=CalcStatus(serviceId,name,currentMileage,intervalKm,intervalMonths,prealertKm,prealertDays,lastKm,lastDate);
                    if(item.Status=="UP_TO_DATE")continue;
                    candidates.Add((r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),currentMileage,item));
                }
            }
            var lastSent=new Dictionary<(Guid,Guid,string),DateTime>();
            await using(var lcmd=new NpgsqlCommand("SELECT vehicle_id,plan_service_id,status,max(created_at) FROM notification_log WHERE company_id=@c GROUP BY vehicle_id,plan_service_id,status",con))
            {
                lcmd.Parameters.AddWithValue("c",companyId);await using var lr=await lcmd.ExecuteReaderAsync();
                while(await lr.ReadAsync())lastSent[(lr.GetGuid(0),lr.IsDBNull(1)?Guid.Empty:lr.GetGuid(1),lr.GetString(2))]=lr.GetDateTime(3);
            }
            var list=candidates.Select(x=>new{
                vehicleId=x.VehicleId,plate=x.Plate,internalNumber=x.InternalNumber,serviceId=x.Item.ServiceId,serviceName=x.Item.Name,status=x.Item.Status,
                currentMileage=x.CurrentMileage,nextDueMileage=x.Item.NextDueMileage,
                lastNotifiedAt=lastSent.TryGetValue((x.VehicleId,x.Item.ServiceId,x.Item.Status),out var sent)?sent:(DateTime?)null
            }).ToList();
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPost("/api/v1/notifications/mark-sent", async (ClaimsPrincipal principal, MarkNotificationRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(req.Status!="DUE_SOON"&&req.Status!="OVERDUE")return Results.BadRequest();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand(@"INSERT INTO notification_log(id,company_id,vehicle_id,plan_service_id,status,channel,recipient,result,created_by_user_id) VALUES(@id,@c,@v,@s,@st,@ch,@to,'RECORDED',@u)",con);
            cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("c",CompanyId(principal));cmd.Parameters.AddWithValue("v",req.VehicleId);cmd.Parameters.AddWithValue("s",req.ServiceId);cmd.Parameters.AddWithValue("st",req.Status);cmd.Parameters.AddWithValue("ch",req.Channel??"INTERNAL");cmd.Parameters.AddWithValue("to",(object?)req.Recipient??DBNull.Value);cmd.Parameters.AddWithValue("u",UserId(principal));await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapGet("/api/v1/notifications/history", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT n.created_at,v.plate,v.internal_number,s.name,n.status,n.channel,n.recipient,n.result FROM notification_log n JOIN vehicles v ON v.id=n.vehicle_id LEFT JOIN maintenance_plan_services s ON s.id=n.plan_service_id WHERE n.company_id=@c ORDER BY n.created_at DESC LIMIT 100";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("c",CompanyId(principal));await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync())list.Add(new{createdAt=r.GetDateTime(0),plate=r.GetString(1),internalNumber=r.IsDBNull(2)?null:r.GetString(2),serviceName=r.IsDBNull(3)?"Servicio":r.GetString(3),status=r.GetString(4),channel=r.GetString(5),recipient=r.IsDBNull(6)?null:r.GetString(6),result=r.GetString(7)});
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();
    }
}
