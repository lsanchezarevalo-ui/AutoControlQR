using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using static Helpers;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, string connectionString, string jwtKey)
    {
        app.MapPost("/api/v1/auth/login", async (LoginRequest req) =>
        {
            await using var con = new NpgsqlConnection(connectionString);
            await con.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT u.id,u.company_id,u.full_name,u.email,u.password_hash,u.role,u.status,c.status FROM users u JOIN companies c ON c.id=u.company_id WHERE lower(u.email)=lower(@email)", con);
            cmd.Parameters.AddWithValue("email", (req.Email ?? "").Trim());
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync() || r.GetString(6) != "ACTIVE" || r.GetString(7)!="ACTIVE") return Results.Unauthorized();

            var user = new DemoUser(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5));
            var hasher = new PasswordHasher<DemoUser>();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();

            var accessToken=CreateJwt(user,jwtKey);
            return Results.Ok(new { success = true, data = new { accessToken, user = new { user.Id, user.FullName, user.Email, user.Role, user.CompanyId } } });
        }).RequireRateLimiting("login");

        app.MapGet("/api/v1/auth/me", (ClaimsPrincipal principal) => Results.Ok(new { success = true, data = new { userId = principal.FindFirstValue("user_id"), companyId = principal.FindFirstValue("company_id"), name = principal.FindFirstValue("name"), role = principal.FindFirstValue("role") } })).RequireAuthorization();

        app.MapPatch("/api/v1/auth/password", async (ClaimsPrincipal principal, ChangeOwnPasswordRequest req) =>
        {
            if(string.IsNullOrWhiteSpace(req.CurrentPassword)||string.IsNullOrWhiteSpace(req.NewPassword)||req.NewPassword.Length<8)
                return Results.BadRequest(new{success=false,error=new{message="La nueva contraseña debe tener mínimo 8 caracteres."}});
            var userId=UserId(principal);
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            DemoUser? user=null;
            await using(var q=new NpgsqlCommand("SELECT id,company_id,full_name,email,password_hash,role FROM users WHERE id=@id AND status='ACTIVE'",con))
            {
                q.Parameters.AddWithValue("id",userId);await using var r=await q.ExecuteReaderAsync();
                if(await r.ReadAsync())user=new DemoUser(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5));
            }
            if(user is null)return Results.NotFound();
            var hasher=new PasswordHasher<DemoUser>();
            if(hasher.VerifyHashedPassword(user,user.PasswordHash,req.CurrentPassword)==PasswordVerificationResult.Failed)
                return Results.BadRequest(new{success=false,error=new{message="La contraseña actual no es correcta."}});
            var updated=user with { PasswordHash="" };
            var hash=hasher.HashPassword(updated,req.NewPassword);
            await using(var cmd=new NpgsqlCommand("UPDATE users SET password_hash=@p WHERE id=@id",con))
            {cmd.Parameters.AddWithValue("p",hash);cmd.Parameters.AddWithValue("id",userId);await cmd.ExecuteNonQueryAsync();}
            return Results.Ok(new{success=true});
        }).RequireAuthorization();
    }
}
