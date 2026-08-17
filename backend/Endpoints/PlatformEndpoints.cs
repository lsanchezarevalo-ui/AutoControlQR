using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using static Helpers;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app, string connectionString)
    {
        app.MapGet("/api/v1/platform/companies", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="PLATFORM_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var sql=@"SELECT c.id,c.name,COALESCE(c.code,''),c.status,c.created_at,
              (SELECT count(*) FROM vehicles v WHERE v.company_id=c.id),
              (SELECT count(*) FROM users u WHERE u.company_id=c.id AND u.status='ACTIVE' AND u.role<>'PLATFORM_ADMIN')
              FROM companies c ORDER BY c.name";
            await using var cmd=new NpgsqlCommand(sql,con);await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),name=r.GetString(1),code=r.GetString(2),status=r.GetString(3),createdAt=r.GetDateTime(4),vehicles=r.GetInt64(5),activeUsers=r.GetInt64(6)});
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapGet("/api/v1/platform/companies/{id:guid}", async (ClaimsPrincipal principal, Guid id) =>
        {
            if(principal.FindFirstValue("role")!="PLATFORM_ADMIN")return Results.Forbid();
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            object? company=null;
            await using(var cmd=new NpgsqlCommand(@"SELECT c.id,c.name,COALESCE(c.code,''),c.status,c.created_at,
              (SELECT count(*) FROM vehicles v WHERE v.company_id=c.id AND v.status='ACTIVE'),
              (SELECT count(*) FROM users u WHERE u.company_id=c.id AND u.status='ACTIVE' AND u.role<>'PLATFORM_ADMIN')
              FROM companies c WHERE c.id=@id",con))
            {
              cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync();
              if(await r.ReadAsync())company=new{id=r.GetGuid(0),name=r.GetString(1),code=r.GetString(2),status=r.GetString(3),createdAt=r.GetDateTime(4),vehicles=r.GetInt64(5),activeUsers=r.GetInt64(6)};
            }
            if(company is null)return Results.NotFound();
            var users=new List<object>();
            await using(var cmd=new NpgsqlCommand("SELECT id,full_name,email,role,status,created_at FROM users WHERE company_id=@id AND role<>'PLATFORM_ADMIN' ORDER BY full_name",con))
            {
              cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync();
              while(await r.ReadAsync())users.Add(new{id=r.GetGuid(0),fullName=r.GetString(1),email=r.GetString(2),role=r.GetString(3),status=r.GetString(4),createdAt=r.GetDateTime(5)});
            }
            var vehicles=new List<object>();
            await using(var cmd=new NpgsqlCommand("SELECT id,plate,internal_number,brand,model,variant,current_mileage,status,mileage_updated_at FROM vehicles WHERE company_id=@id ORDER BY status,internal_number NULLS LAST,plate",con))
            {
              cmd.Parameters.AddWithValue("id",id);await using var r=await cmd.ExecuteReaderAsync();
              while(await r.ReadAsync())vehicles.Add(new{id=r.GetGuid(0),plate=r.GetString(1),internalNumber=r.IsDBNull(2)?null:r.GetString(2),brand=r.GetString(3),model=r.GetString(4),variant=r.IsDBNull(5)?null:r.GetString(5),currentMileage=r.GetInt32(6),status=r.GetString(7),mileageUpdatedAt=r.GetDateTime(8)});
            }
            return Results.Ok(new{success=true,data=new{company,users,vehicles}});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/platform/companies/{companyId:guid}/admins/{userId:guid}/password", async (ClaimsPrincipal principal, Guid companyId, Guid userId, ResetUserPasswordRequest req) =>
        {
            if(principal.FindFirstValue("role")!="PLATFORM_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Password)||req.Password.Length<8)
                return Results.BadRequest(new{success=false,error=new{message="La contraseña debe tener mínimo 8 caracteres."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            DemoUser? user=null;
            await using(var q=new NpgsqlCommand("SELECT id,company_id,full_name,email,role FROM users WHERE id=@id AND company_id=@c AND role='COMPANY_ADMIN'",con))
            {
                q.Parameters.AddWithValue("id",userId);q.Parameters.AddWithValue("c",companyId);await using var r=await q.ExecuteReaderAsync();
                if(await r.ReadAsync())user=new DemoUser(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),"",r.GetString(4));
            }
            if(user is null)return Results.NotFound();
            var hash=new PasswordHasher<DemoUser>().HashPassword(user,req.Password);
            await using(var cmd=new NpgsqlCommand("UPDATE users SET password_hash=@p WHERE id=@id",con))
            {cmd.Parameters.AddWithValue("p",hash);cmd.Parameters.AddWithValue("id",userId);await cmd.ExecuteNonQueryAsync();}
            return Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPost("/api/v1/platform/companies", async (ClaimsPrincipal principal, CreateCompanyRequest req) =>
        {
            if(principal.FindFirstValue("role")!="PLATFORM_ADMIN")return Results.Forbid();
            if(string.IsNullOrWhiteSpace(req.Name)||string.IsNullOrWhiteSpace(req.AdminName)||string.IsNullOrWhiteSpace(req.AdminEmail)||string.IsNullOrWhiteSpace(req.AdminPassword)||req.AdminPassword.Length<8)
              return Results.BadRequest(new{success=false,error=new{message="Empresa, administrador, correo y contraseña de mínimo 8 caracteres son obligatorios."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            try{
              var companyId=Guid.NewGuid();
              await using var seq=new NpgsqlCommand("SELECT nextval('company_code_seq')",con,tx);
              var companyNumber=Convert.ToInt64(await seq.ExecuteScalarAsync());
              var code=$"EMP-{companyNumber:0000}";
              await using(var c=new NpgsqlCommand("INSERT INTO companies(id,name,code,status) VALUES(@id,@n,@code,'ACTIVE')",con,tx)){c.Parameters.AddWithValue("id",companyId);c.Parameters.AddWithValue("n",req.Name.Trim());c.Parameters.AddWithValue("code",(object?)code??DBNull.Value);await c.ExecuteNonQueryAsync();}
              var id=Guid.NewGuid();var user=new DemoUser(id,companyId,req.AdminName.Trim(),req.AdminEmail.Trim().ToLowerInvariant(),"","COMPANY_ADMIN");var hash=new PasswordHasher<DemoUser>().HashPassword(user,req.AdminPassword);
              await using(var u=new NpgsqlCommand("INSERT INTO users(id,company_id,full_name,email,password_hash,role,status) VALUES(@id,@c,@n,@e,@p,'COMPANY_ADMIN','ACTIVE')",con,tx)){u.Parameters.AddWithValue("id",id);u.Parameters.AddWithValue("c",companyId);u.Parameters.AddWithValue("n",user.FullName);u.Parameters.AddWithValue("e",user.Email);u.Parameters.AddWithValue("p",hash);await u.ExecuteNonQueryAsync();}
              await tx.CommitAsync();return Results.Ok(new{success=true,data=new{id=companyId,name=req.Name.Trim(),code,adminEmail=user.Email}});
            }catch(PostgresException ex) when(ex.SqlState=="23505"){await tx.RollbackAsync();return Results.Conflict(new{success=false,error=new{message="El código de empresa o correo ya existe."}});}
        }).RequireAuthorization();

        app.MapPatch("/api/v1/platform/companies/{id:guid}/status", async (ClaimsPrincipal principal,Guid id,CompanyStatusRequest req) =>
        {
            if(principal.FindFirstValue("role")!="PLATFORM_ADMIN")return Results.Forbid();
            var status=req.Status?.Trim().ToUpperInvariant();if(status!="ACTIVE"&&status!="INACTIVE")return Results.BadRequest(new{success=false,error=new{message="Estado no válido."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
            await using(var c=new NpgsqlCommand("UPDATE companies SET status=@s WHERE id=@id",con,tx)){c.Parameters.AddWithValue("s",status);c.Parameters.AddWithValue("id",id);if(await c.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
            if(status=="INACTIVE"){await using var u=new NpgsqlCommand("UPDATE users SET status='INACTIVE' WHERE company_id=@id AND role<>'PLATFORM_ADMIN'",con,tx);u.Parameters.AddWithValue("id",id);await u.ExecuteNonQueryAsync();}
            await tx.CommitAsync();return Results.Ok(new{success=true});
        }).RequireAuthorization();
    }
}
