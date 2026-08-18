using System.Security.Claims;
using Npgsql;
using static Helpers;

public static class ServiceCatalogEndpoints
{
    static object CompanyServiceObject(NpgsqlDataReader r) => new{id=r.GetGuid(0),name=r.GetString(1),category=r.GetString(2),specification=r.IsDBNull(3)?null:r.GetString(3),defaultIntervalKm=r.IsDBNull(4)?(int?)null:r.GetInt32(4),defaultIntervalMonths=r.IsDBNull(5)?(int?)null:r.GetInt32(5),defaultPrealertKm=r.IsDBNull(6)?(int?)null:r.GetInt32(6),defaultPrealertDays=r.IsDBNull(7)?(int?)null:r.GetInt32(7)};

    public static void MapServiceCatalogEndpoints(this WebApplication app, string connectionString)
    {
        app.MapGet("/api/v1/service-catalog", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT id,name,category,specification,default_interval_km,default_interval_months,default_prealert_km,default_prealert_days
                      FROM company_services WHERE company_id=@c AND active=true ORDER BY name";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
            await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync())list.Add(CompanyServiceObject(r));
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapGet("/api/v1/service-catalog/archived", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT id,name,category,specification,default_interval_km,default_interval_months,default_prealert_km,default_prealert_days
                      FROM company_services WHERE company_id=@c AND active=false ORDER BY name";
            await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
            await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync())list.Add(CompanyServiceObject(r));
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPost("/api/v1/service-catalog", async (ClaimsPrincipal principal, CreateCompanyServiceRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
            if(req.DefaultIntervalKm is null && req.DefaultIntervalMonths is null)return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
            if(req.DefaultIntervalKm.HasValue && req.DefaultIntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
            if(req.DefaultPrealertKm.HasValue && req.DefaultPrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
            var companyId=CompanyId(principal);var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using(var exists=new NpgsqlCommand("SELECT 1 FROM company_services WHERE company_id=@c AND lower(name)=lower(@n) AND active=true",con))
            {exists.Parameters.AddWithValue("c",companyId);exists.Parameters.AddWithValue("n",req.Name.Trim());if(await exists.ExecuteScalarAsync() is not null)return Results.Conflict(new{success=false,error=new{message="Ya existe un servicio con ese nombre."}});}
            var id=await ResolveOrCreateCompanyService(con,null,companyId,userId,req.Name,req.Category,req.Specification,req.DefaultIntervalKm,req.DefaultIntervalMonths,req.DefaultPrealertKm,req.DefaultPrealertDays);
            return Results.Ok(new{success=true,data=new{id}});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/service-catalog/{id:guid}", async (ClaimsPrincipal principal, Guid id, UpdateCompanyServiceRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
            if(req.DefaultIntervalKm is null && req.DefaultIntervalMonths is null)return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
            if(req.DefaultIntervalKm.HasValue && req.DefaultIntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
            if(req.DefaultPrealertKm.HasValue && req.DefaultPrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using(var dup=new NpgsqlCommand("SELECT 1 FROM company_services WHERE company_id=@c AND lower(name)=lower(@n) AND active=true AND id<>@id",con))
            {dup.Parameters.AddWithValue("c",companyId);dup.Parameters.AddWithValue("n",req.Name.Trim());dup.Parameters.AddWithValue("id",id);if(await dup.ExecuteScalarAsync() is not null)return Results.Conflict(new{success=false,error=new{message="Ya existe otro servicio con ese nombre."}});}
            var sql=@"UPDATE company_services SET name=@n,category=@cat,specification=@sp,default_interval_km=@ik,default_interval_months=@im,default_prealert_km=@pk,default_prealert_days=@pd,updated_at=now()
                      WHERE id=@id AND company_id=@c";
            await using var cmd=new NpgsqlCommand(sql,con);
            cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("cat",string.IsNullOrWhiteSpace(req.Category)?"General":req.Category.Trim());cmd.Parameters.AddWithValue("sp",(object?)req.Specification?.Trim()??DBNull.Value);
            cmd.Parameters.AddWithValue("ik",(object?)req.DefaultIntervalKm??DBNull.Value);cmd.Parameters.AddWithValue("im",(object?)req.DefaultIntervalMonths??DBNull.Value);cmd.Parameters.AddWithValue("pk",(object?)req.DefaultPrealertKm??DBNull.Value);cmd.Parameters.AddWithValue("pd",(object?)req.DefaultPrealertDays??DBNull.Value);
            cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("c",companyId);
            if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/service-catalog/{id:guid}/archive", async (ClaimsPrincipal principal, Guid id) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand("UPDATE company_services SET active=false,updated_at=now() WHERE id=@id AND company_id=@c AND active=true",con);
            cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("c",CompanyId(principal));
            if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/service-catalog/{id:guid}/reactivate", async (ClaimsPrincipal principal, Guid id) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            string name;
            await using(var nc=new NpgsqlCommand("SELECT name FROM company_services WHERE id=@id AND company_id=@c AND active=false",con))
            {nc.Parameters.AddWithValue("id",id);nc.Parameters.AddWithValue("c",companyId);var x=await nc.ExecuteScalarAsync();if(x is null)return Results.NotFound();name=(string)x;}
            await using(var dup=new NpgsqlCommand("SELECT 1 FROM company_services WHERE company_id=@c AND lower(name)=lower(@n) AND active=true",con))
            {dup.Parameters.AddWithValue("c",companyId);dup.Parameters.AddWithValue("n",name);if(await dup.ExecuteScalarAsync() is not null)return Results.Conflict(new{success=false,error=new{message="Ya existe un servicio activo con ese nombre. Renómbralo antes de reactivar este."}});}
            await using var cmd=new NpgsqlCommand("UPDATE company_services SET active=true,updated_at=now() WHERE id=@id AND company_id=@c",con);
            cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("c",companyId);await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new{success=true});
        }).RequireAuthorization();
    }
}
