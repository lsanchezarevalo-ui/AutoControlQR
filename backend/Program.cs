using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using static Schema;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing DB connection string");
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing JWT key");
var publicWebBaseUrl = builder.Configuration["PublicWebBaseUrl"] ?? "http://localhost:3000";
var corsOrigin = publicWebBaseUrl.TrimEnd('/');

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();

var forwardedHeaderOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
forwardedHeaderOptions.KnownNetworks.Clear();
forwardedHeaderOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaderOptions);

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(new { success = false, error = new { code = "INTERNAL_ERROR", message = "Ocurrió un error inesperado. Intenta nuevamente." } });
        }
    }
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (ctx,next) =>
{
    if(ctx.User.Identity?.IsAuthenticated==true)
    {
        var uid=ctx.User.FindFirstValue("user_id");var cid=ctx.User.FindFirstValue("company_id");
        if(Guid.TryParse(uid,out var userId)&&Guid.TryParse(cid,out var companyId))
        {
            await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
            await using var cmd=new NpgsqlCommand("SELECT 1 FROM users u JOIN companies c ON c.id=u.company_id WHERE u.id=@u AND u.company_id=@c AND u.status='ACTIVE' AND c.status='ACTIVE'",con);
            cmd.Parameters.AddWithValue("u",userId);cmd.Parameters.AddWithValue("c",companyId);
            if(await cmd.ExecuteScalarAsync() is null){ctx.Response.StatusCode=401;await ctx.Response.WriteAsJsonAsync(new{success=false,error=new{code="SESSION_INACTIVE",message="Usuario o empresa inactiva."}});return;}
        }
    }
    await next();
});

await EnsureDatabaseReady(connectionString);
await EnsureV5Schema(connectionString);
await EnsureV6Schema(connectionString);
await EnsureV12Schema(connectionString);
await EnsureV30Schema(connectionString);
await EnsureV31IndividualServicesSchema(connectionString);
await EnsureV32ServiceCatalogSchema(connectionString);
await EnsureDemoData(connectionString);

app.MapGet("/api/v1/health", () => Results.Ok(new { success = true, service = "AutoControlQR.Api", version = "v31.6" }));

app.MapAuthEndpoints(connectionString, jwtKey);
app.MapCompanyEndpoints(connectionString);
app.MapPlatformEndpoints(connectionString);
app.MapUserEndpoints(connectionString);
app.MapTechnicianEndpoints(connectionString, jwtKey);
app.MapVehicleEndpoints(connectionString);
app.MapPlanEndpoints(connectionString);
app.MapServiceCatalogEndpoints(connectionString);
app.MapMaintenanceEndpoints(connectionString);
app.MapReportEndpoints(connectionString);
app.MapPublicEndpoints(connectionString, publicWebBaseUrl);

app.Run();
