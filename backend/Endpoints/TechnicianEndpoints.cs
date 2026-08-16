using System.Security.Claims;
using Npgsql;
using static Helpers;

public static class TechnicianEndpoints
{
    public static void MapTechnicianEndpoints(this WebApplication app, string connectionString, string jwtKey)
    {
        // FLUJO DE TRABAJO DEL TÉCNICO V11.1
        app.MapGet("/api/v1/technician/vehicle-lookup", async (ClaimsPrincipal principal,string search) =>
        {
            if(principal.FindFirstValue("role")!="TECHNICIAN")return Results.Forbid();
            search=(search??"").Trim();if(search.Length<2)return Results.BadRequest(new{success=false,error=new{message="Escribe al menos 2 caracteres de placa o número interno."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT id,plate,internal_number,brand,model,variant,current_mileage
                      FROM vehicles WHERE company_id=@c AND status='ACTIVE'
                        AND (plate ILIKE @q OR internal_number ILIKE @q)
                      ORDER BY CASE WHEN lower(plate)=lower(@exact) OR lower(coalesce(internal_number,''))=lower(@exact) THEN 0 ELSE 1 END,plate
                      LIMIT 10";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("c",CompanyId(principal));cmd.Parameters.AddWithValue("q","%"+search+"%");cmd.Parameters.AddWithValue("exact",search);
            await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),plate=r.GetString(1),internalNumber=r.IsDBNull(2)?null:r.GetString(2),brand=r.GetString(3),model=r.GetString(4),variant=r.IsDBNull(5)?null:r.GetString(5),currentMileage=r.GetInt32(6)});
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPost("/api/v1/technician/select-vehicle", async (ClaimsPrincipal principal, TechnicianSelectVehicleRequest req) =>
        {
            if(principal.FindFirstValue("role")!="TECHNICIAN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            Guid? vehicleId=req.VehicleId;
            if(!string.IsNullOrWhiteSpace(req.QrToken))
            {
                await using var q=new NpgsqlCommand(@"SELECT v.id FROM vehicle_qr_tokens qt JOIN vehicles v ON v.id=qt.vehicle_id
                    WHERE qt.token=@t AND qt.status='ACTIVE' AND v.company_id=@c AND v.status='ACTIVE'",con);
                q.Parameters.AddWithValue("t",req.QrToken.Trim());q.Parameters.AddWithValue("c",CompanyId(principal));
                var x=await q.ExecuteScalarAsync();if(x is Guid g)vehicleId=g;
            }
            if(!vehicleId.HasValue)return Results.NotFound();
            string plate;string? internalNumber;string brand;string model;string? variant;int km;
            await using(var c=new NpgsqlCommand("SELECT plate,internal_number,brand,model,variant,current_mileage FROM vehicles WHERE id=@v AND company_id=@c AND status='ACTIVE'",con))
            {
                c.Parameters.AddWithValue("v",vehicleId.Value);c.Parameters.AddWithValue("c",CompanyId(principal));
                await using var r=await c.ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound();
                plate=r.GetString(0);internalNumber=r.IsDBNull(1)?null:r.GetString(1);brand=r.GetString(2);model=r.GetString(3);variant=r.IsDBNull(4)?null:r.GetString(4);km=r.GetInt32(5);
            }
            var user=new DemoUser(UserId(principal),CompanyId(principal),principal.FindFirstValue("name")??"Técnico","", "", "TECHNICIAN");
            var accessToken=CreateJwt(user,jwtKey,vehicleId.Value);
            return Results.Ok(new{success=true,data=new{accessToken,vehicle=new{id=vehicleId.Value,plate,internalNumber,brand,model,variant,currentMileage=km}}});
        }).RequireAuthorization();

        app.MapPost("/api/v1/technician/clear-vehicle", (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="TECHNICIAN")return Results.Forbid();
            var user=new DemoUser(UserId(principal),CompanyId(principal),principal.FindFirstValue("name")??"Técnico","", "", "TECHNICIAN");
            return Results.Ok(new{success=true,data=new{accessToken=CreateJwt(user,jwtKey)}});
        }).RequireAuthorization();
    }
}
