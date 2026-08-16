using System.Security.Claims;
using Npgsql;
using static Helpers;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app, string connectionString)
    {
        // ULTIMOS SERVICIOS V10.2
        app.MapGet("/api/v1/reports/latest-services", async (ClaimsPrincipal principal, Guid? vehicleId, string? service) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"WITH ranked AS (
                SELECT mr.service_date,mr.mileage,mr.notes,u.full_name,
                       v.id vehicle_id,v.plate,v.internal_number,v.brand,v.model,v.variant,
                       mri.service_name_snapshot,mri.next_due_mileage,mri.next_due_date,
                       ROW_NUMBER() OVER(PARTITION BY v.id,lower(mri.service_name_snapshot)
                                         ORDER BY mr.service_date DESC,mr.created_at DESC) rn
                FROM maintenance_records mr
                JOIN vehicles v ON v.id=mr.vehicle_id
                JOIN maintenance_record_items mri ON mri.maintenance_record_id=mr.id
                LEFT JOIN users u ON u.id=mr.technician_user_id
                WHERE mr.company_id=@c
                  AND (@v::uuid IS NULL OR mr.vehicle_id=@v)
                  AND (@s='' OR lower(mri.service_name_snapshot)=lower(@s))
            )
            SELECT service_date,mileage,notes,full_name,vehicle_id,plate,internal_number,brand,model,variant,
                   service_name_snapshot,next_due_mileage,next_due_date
            FROM ranked WHERE rn=1
            ORDER BY plate,service_name_snapshot";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("c",companyId);
            cmd.Parameters.AddWithValue("v",NpgsqlTypes.NpgsqlDbType.Uuid,(object?)vehicleId??DBNull.Value);
            cmd.Parameters.AddWithValue("s",service?.Trim()??"");
            await using var r=await cmd.ExecuteReaderAsync();var rows=new List<object>();
            while(await r.ReadAsync())rows.Add(new{
              serviceDate=r.GetDateTime(0),mileage=r.GetInt32(1),notes=r.IsDBNull(2)?null:r.GetString(2),technician=r.IsDBNull(3)?null:r.GetString(3),
              vehicleId=r.GetGuid(4),plate=r.GetString(5),internalNumber=r.IsDBNull(6)?null:r.GetString(6),brand=r.GetString(7),model=r.GetString(8),variant=r.IsDBNull(9)?null:r.GetString(9),
              serviceName=r.GetString(10),nextDueMileage=r.IsDBNull(11)?(int?)null:r.GetInt32(11),nextDueDate=r.IsDBNull(12)?(DateTime?)null:r.GetDateTime(12)
            });
            return Results.Ok(new{success=true,data=new{count=rows.Count,rows}});
        }).RequireAuthorization();

        // REPORTES DE FLOTA V10
        app.MapGet("/api/v1/reports/maintenance", async (ClaimsPrincipal principal, DateTime? from, DateTime? to, Guid? vehicleId, string? service) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);
            var dateFrom=(from??DateTime.UtcNow.Date.AddDays(-30)).Date;
            var dateTo=(to??DateTime.UtcNow.Date).Date;
            if(dateTo<dateFrom)return Results.BadRequest(new{success=false,error=new{message="La fecha final no puede ser anterior a la inicial."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();

            var sql=@"SELECT mr.id,mr.service_date,mr.mileage,mr.notes,u.full_name,
                             v.id,v.plate,v.internal_number,v.brand,v.model,v.variant,
                             mri.service_name_snapshot,mri.next_due_mileage,mri.next_due_date
                      FROM maintenance_records mr
                      JOIN vehicles v ON v.id=mr.vehicle_id
                      JOIN maintenance_record_items mri ON mri.maintenance_record_id=mr.id
                      LEFT JOIN users u ON u.id=mr.technician_user_id
                      WHERE mr.company_id=@c AND mr.service_date>=@f AND mr.service_date<=@t
                        AND (@v::uuid IS NULL OR mr.vehicle_id=@v)
                        AND (@s='' OR lower(mri.service_name_snapshot)=lower(@s))
                      ORDER BY mr.service_date DESC,mr.created_at DESC,v.plate,mri.service_name_snapshot";
            await using var cmd=new NpgsqlCommand(sql,con);
            cmd.Parameters.AddWithValue("c",companyId);cmd.Parameters.AddWithValue("f",dateFrom);cmd.Parameters.AddWithValue("t",dateTo);
            cmd.Parameters.AddWithValue("v",NpgsqlTypes.NpgsqlDbType.Uuid,(object?)vehicleId??DBNull.Value);
            cmd.Parameters.AddWithValue("s",service?.Trim()??"");
            await using var r=await cmd.ExecuteReaderAsync();var rows=new List<object>();
            while(await r.ReadAsync())rows.Add(new{
                recordId=r.GetGuid(0),serviceDate=r.GetDateTime(1),mileage=r.GetInt32(2),notes=r.IsDBNull(3)?null:r.GetString(3),technician=r.IsDBNull(4)?null:r.GetString(4),
                vehicleId=r.GetGuid(5),plate=r.GetString(6),internalNumber=r.IsDBNull(7)?null:r.GetString(7),brand=r.GetString(8),model=r.GetString(9),variant=r.IsDBNull(10)?null:r.GetString(10),
                serviceName=r.GetString(11),nextDueMileage=r.IsDBNull(12)?(int?)null:r.GetInt32(12),nextDueDate=r.IsDBNull(13)?(DateTime?)null:r.GetDateTime(13)
            });
            return Results.Ok(new{success=true,data=new{from=dateFrom,to=dateTo,count=rows.Count,rows}});
        }).RequireAuthorization();
    }
}
