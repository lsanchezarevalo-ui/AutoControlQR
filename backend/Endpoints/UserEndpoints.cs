using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using static Helpers;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app, string connectionString)
    {
        // USUARIOS Y ROLES V11
        app.MapGet("/api/v1/users", async (ClaimsPrincipal principal) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var companyId=CompanyId(principal);await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand("SELECT id,full_name,email,role,status,created_at FROM users WHERE company_id=@c ORDER BY full_name",con);cmd.Parameters.AddWithValue("c",companyId);
            await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
            while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),fullName=r.GetString(1),email=r.GetString(2),role=r.GetString(3),status=r.GetString(4),createdAt=r.GetDateTime(5)});
            return Results.Ok(new{success=true,data=list});
        }).RequireAuthorization();

        app.MapPost("/api/v1/users", async (ClaimsPrincipal principal, CreateUserRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var role=req.Role?.Trim().ToUpperInvariant();if(role!="COMPANY_ADMIN"&&role!="TECHNICIAN")return Results.BadRequest(new{success=false,error=new{message="Rol no válido."}});
            if(string.IsNullOrWhiteSpace(req.FullName)||string.IsNullOrWhiteSpace(req.Email)||string.IsNullOrWhiteSpace(req.Password)||req.Password.Length<8)return Results.BadRequest(new{success=false,error=new{message="Nombre, correo y contraseña de mínimo 8 caracteres son obligatorios."}});
            var companyId=CompanyId(principal);await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            var id=Guid.NewGuid();var user=new DemoUser(id,companyId,req.FullName.Trim(),req.Email.Trim().ToLowerInvariant(),"",role);var hash=new PasswordHasher<DemoUser>().HashPassword(user,req.Password);
            try{await using var cmd=new NpgsqlCommand("INSERT INTO users(id,company_id,full_name,email,password_hash,role,status) VALUES(@id,@c,@n,@e,@p,@r,'ACTIVE')",con);
            cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("c",companyId);cmd.Parameters.AddWithValue("n",user.FullName);cmd.Parameters.AddWithValue("e",user.Email);cmd.Parameters.AddWithValue("p",hash);cmd.Parameters.AddWithValue("r",role);await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new{success=true,data=new{id,user.FullName,user.Email,role,status="ACTIVE"}});}
            catch(PostgresException ex) when(ex.SqlState=="23505"){return Results.Conflict(new{success=false,error=new{message="Ya existe un usuario con ese correo."}});}
        }).RequireAuthorization();

        app.MapPatch("/api/v1/users/{id:guid}/status", async (ClaimsPrincipal principal, Guid id, UserStatusRequest req) =>
        {
            if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
            var status=req.Status?.Trim().ToUpperInvariant();if(status!="ACTIVE"&&status!="INACTIVE")return Results.BadRequest(new{success=false,error=new{message="Estado no válido."}});
            if(id==UserId(principal)&&status=="INACTIVE")return Results.BadRequest(new{success=false,error=new{message="No puedes desactivar tu propio usuario."}});
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var cmd=new NpgsqlCommand("UPDATE users SET status=@s WHERE id=@id AND company_id=@c",con);
            cmd.Parameters.AddWithValue("s",status);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("c",CompanyId(principal));var n=await cmd.ExecuteNonQueryAsync();return n==0?Results.NotFound():Results.Ok(new{success=true});
        }).RequireAuthorization();

        app.MapPatch("/api/v1/users/{id:guid}", async (ClaimsPrincipal principal,Guid id,UpdateUserRequest req) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         var role=req.Role?.Trim().ToUpperInvariant();if(role!="COMPANY_ADMIN"&&role!="TECHNICIAN")return Results.BadRequest(new{success=false,error=new{message="Rol no válido."}});
         if(string.IsNullOrWhiteSpace(req.FullName)||string.IsNullOrWhiteSpace(req.Email))return Results.BadRequest(new{success=false,error=new{message="Nombre y correo son obligatorios."}});
         if(id==UserId(principal)&&role!="COMPANY_ADMIN")return Results.BadRequest(new{success=false,error=new{message="No puedes quitarte a ti mismo el rol de administrador."}});
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
         try{await using var cmd=new NpgsqlCommand("UPDATE users SET full_name=@n,email=@e,role=@r WHERE id=@id AND company_id=@c",con);cmd.Parameters.AddWithValue("n",req.FullName.Trim());cmd.Parameters.AddWithValue("e",req.Email.Trim().ToLowerInvariant());cmd.Parameters.AddWithValue("r",role);cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();return Results.Ok(new{success=true});}
         catch(PostgresException ex) when(ex.SqlState=="23505"){return Results.Conflict(new{success=false,error=new{message="Ya existe un usuario con ese correo."}});}
        }).RequireAuthorization();

        app.MapPatch("/api/v1/users/{id:guid}/password", async (ClaimsPrincipal principal,Guid id,ResetUserPasswordRequest req) =>
        {
         if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
         if(string.IsNullOrWhiteSpace(req.Password)||req.Password.Length<8)return Results.BadRequest(new{success=false,error=new{message="La contraseña debe tener mínimo 8 caracteres."}});
         await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
         DemoUser? user=null;await using(var q=new NpgsqlCommand("SELECT id,company_id,full_name,email,role FROM users WHERE id=@id AND company_id=@c",con)){q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("c",CompanyId(principal));await using var r=await q.ExecuteReaderAsync();if(await r.ReadAsync())user=new DemoUser(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),"",r.GetString(4));}
         if(user is null)return Results.NotFound();var hash=new PasswordHasher<DemoUser>().HashPassword(user,req.Password);
         await using(var cmd=new NpgsqlCommand("UPDATE users SET password_hash=@p WHERE id=@id",con)){cmd.Parameters.AddWithValue("p",hash);cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();}
         return Results.Ok(new{success=true});
        }).RequireAuthorization();
    }
}
