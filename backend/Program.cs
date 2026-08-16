using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;
using QRCoder;

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
await EnsureDemoData(connectionString);

app.MapGet("/api/v1/health", () => Results.Ok(new { success = true, service = "AutoControlQR.Api", version = "v31.6" }));

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



// MULTIEMPRESA V12
app.MapGet("/api/v1/company", async (ClaimsPrincipal principal) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    await using var cmd=new NpgsqlCommand(@"SELECT id,name,COALESCE(code,''),status,legal_name,tax_id,phone,email,address,logo_data_url,
 notification_enabled,notification_internal,notification_email_enabled,notification_email,notification_due_soon,notification_overdue,notification_repeat_days
 FROM companies WHERE id=@c",con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
    await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound();
    return Results.Ok(new{success=true,data=new{id=r.GetGuid(0),name=r.GetString(1),code=r.GetString(2),status=r.GetString(3),legalName=r.IsDBNull(4)?null:r.GetString(4),taxId=r.IsDBNull(5)?null:r.GetString(5),phone=r.IsDBNull(6)?null:r.GetString(6),email=r.IsDBNull(7)?null:r.GetString(7),address=r.IsDBNull(8)?null:r.GetString(8),logoDataUrl=r.IsDBNull(9)?null:r.GetString(9),notificationEnabled=r.GetBoolean(10),notificationInternal=r.GetBoolean(11),notificationEmailEnabled=r.GetBoolean(12),notificationEmail=r.IsDBNull(13)?null:r.GetString(13),notificationDueSoon=r.GetBoolean(14),notificationOverdue=r.GetBoolean(15),notificationRepeatDays=r.GetInt32(16)}});
}).RequireAuthorization();


app.MapPatch("/api/v1/company", async (ClaimsPrincipal principal, UpdateCompanyRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre de la empresa es obligatorio."}});
    if(!string.IsNullOrEmpty(req.LogoDataUrl) && (req.LogoDataUrl.Length>2_000_000 || !System.Text.RegularExpressions.Regex.IsMatch(req.LogoDataUrl,@"^data:image/(png|jpeg|jpg|webp|gif);base64,[A-Za-z0-9+/]+={0,2}$")))
        return Results.BadRequest(new{success=false,error=new{message="El logo debe ser una imagen válida (PNG, JPG, WEBP o GIF) de tamaño razonable."}});
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    await using var cmd=new NpgsqlCommand(@"UPDATE companies SET name=@n,legal_name=@l,tax_id=@t,phone=@p,email=@e,address=@a,logo_data_url=@logo WHERE id=@c",con);
    cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("l",(object?)req.LegalName?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("t",(object?)req.TaxId?.Trim()??DBNull.Value);
    cmd.Parameters.AddWithValue("p",(object?)req.Phone?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("e",(object?)req.Email?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("a",(object?)req.Address?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("logo",(object?)req.LogoDataUrl??DBNull.Value);cmd.Parameters.AddWithValue("c",CompanyId(principal));
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

app.MapGet("/api/v1/platform/companies", async (ClaimsPrincipal principal) =>
{
    if(principal.FindFirstValue("role")!="PLATFORM_ADMIN")return Results.Forbid();
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    var sql=@"SELECT c.id,c.name,COALESCE(c.code,''),c.status,c.created_at,
      (SELECT count(*) FROM vehicles v WHERE v.company_id=c.id),
      (SELECT count(*) FROM users u WHERE u.company_id=c.id AND u.status='ACTIVE')
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
      (SELECT count(*) FROM users u WHERE u.company_id=c.id AND u.status='ACTIVE')
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

app.MapGet("/api/v1/vehicles", async (ClaimsPrincipal principal, string? search) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    try
    {
        var companyId = CompanyId(principal);
        await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync();
        var sql = @"SELECT v.id,v.plate,v.internal_number,v.brand,v.model,v.variant,v.current_mileage,v.mileage_updated_at,q.token,
                           CASE WHEN COALESCE(mp.is_vehicle_specific,false) THEN NULL ELSE mp.name END,
                           CASE WHEN COALESCE(mp.is_vehicle_specific,false) THEN NULL ELSE mpv.version_number END,
                           CASE WHEN COALESCE(mp.is_vehicle_specific,false) THEN NULL ELSE a.plan_version_id END
                    FROM vehicles v
                    LEFT JOIN vehicle_qr_tokens q ON q.vehicle_id=v.id AND q.status='ACTIVE'
                    LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
                    LEFT JOIN maintenance_plan_versions mpv ON mpv.id=a.plan_version_id
                    LEFT JOIN maintenance_plans mp ON mp.id=mpv.maintenance_plan_id
                    WHERE v.company_id=@company AND v.status='ACTIVE'
                      AND (@search IS NULL OR v.plate ILIKE '%'||@search||'%' OR v.internal_number ILIKE '%'||@search||'%')
                    ORDER BY v.internal_number NULLS LAST, v.plate LIMIT 200";
        await using var cmd = new NpgsqlCommand(sql, con);
        cmd.Parameters.AddWithValue("company", companyId);
        cmd.Parameters.Add("search", NpgsqlDbType.Text).Value = (object?)search ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<object>();
        while (await r.ReadAsync()) list.Add(new {
            id=r.GetGuid(0), plate=r.GetString(1), internalNumber=r.IsDBNull(2)?null:r.GetString(2), brand=r.GetString(3), model=r.GetString(4), variant=r.IsDBNull(5)?null:r.GetString(5),
            currentMileage=r.GetInt32(6), mileageUpdatedAt=r.GetDateTime(7), qrToken=r.IsDBNull(8)?null:r.GetString(8),
            planName=r.IsDBNull(9)?null:r.GetString(9), planVersion=r.IsDBNull(10)?(int?)null:r.GetInt32(10), planVersionId=r.IsDBNull(11)?(Guid?)null:r.GetGuid(11)
        });
        return Results.Ok(new { success = true, data = list });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error loading vehicles");
        return Results.Json(new { success=false, error=new { code="VEHICLES_LOAD_FAILED", message="No fue posible cargar la lista de vehículos." } }, statusCode:500);
    }
}).RequireAuthorization();

app.MapPost("/api/v1/vehicles", async (ClaimsPrincipal principal, CreateVehicleRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Plate)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
        return Results.BadRequest(new{success=false,error=new{message="Placa, marca y modelo son obligatorios."}});
    if(req.CurrentMileage<0)
        return Results.BadRequest(new{success=false,error=new{message="El kilometraje no puede ser negativo."}});
    var companyId = CompanyId(principal); var userId = UserId(principal);
    await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync(); await using var tx = await con.BeginTransactionAsync();
    try
    {
        if(req.PlanVersionId.HasValue)
        {
            await using var planCheck=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@pv AND p.company_id=@c AND p.status='ACTIVE' AND pv.status='PUBLISHED'",con,tx);
            planCheck.Parameters.AddWithValue("pv",req.PlanVersionId.Value);planCheck.Parameters.AddWithValue("c",companyId);
            if(await planCheck.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="El plan seleccionado no es válido."}});}
        }
        var vehicleId = Guid.NewGuid();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        await using (var cmd = new NpgsqlCommand(@"INSERT INTO vehicles(id,company_id,plate,internal_number,brand,model,variant,current_mileage,mileage_updated_at) VALUES(@id,@company,@plate,@internal,@brand,@model,@variant,@km,@now)", con, tx))
        { cmd.Parameters.AddWithValue("id",vehicleId); cmd.Parameters.AddWithValue("company",companyId); cmd.Parameters.AddWithValue("plate",req.Plate.Trim().ToUpperInvariant()); cmd.Parameters.AddWithValue("internal",(object?)req.InternalNumber?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue("brand",req.Brand.Trim()); cmd.Parameters.AddWithValue("model",req.Model.Trim()); cmd.Parameters.AddWithValue("variant",(object?)req.Variant?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue("km",req.CurrentMileage); cmd.Parameters.AddWithValue("now",now); await cmd.ExecuteNonQueryAsync(); }
        await using (var cmd = new NpgsqlCommand("INSERT INTO mileage_readings(vehicle_id,mileage,source,created_by_user_id) VALUES(@v,@km,'INITIAL',@u)",con,tx))
        { cmd.Parameters.AddWithValue("v",vehicleId); cmd.Parameters.AddWithValue("km",req.CurrentMileage); cmd.Parameters.AddWithValue("u",userId); await cmd.ExecuteNonQueryAsync(); }
        await using (var cmd = new NpgsqlCommand("INSERT INTO vehicle_qr_tokens(vehicle_id,token) VALUES(@v,@t)",con,tx))
        { cmd.Parameters.AddWithValue("v",vehicleId); cmd.Parameters.AddWithValue("t",token); await cmd.ExecuteNonQueryAsync(); }
        if(req.PlanVersionId.HasValue)
        {
            await using var cmd = new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx);
            cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("p",req.PlanVersionId.Value);cmd.Parameters.AddWithValue("u",userId);await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand("INSERT INTO audit_logs(company_id,user_id,entity_type,entity_id,action,new_values,source) VALUES(@c,@u,'VEHICLE',@v,'CREATE',CAST(@json AS jsonb),'WEB')",con,tx))
        { cmd.Parameters.AddWithValue("c",companyId); cmd.Parameters.AddWithValue("u",userId); cmd.Parameters.AddWithValue("v",vehicleId); cmd.Parameters.AddWithValue("json",JsonSerializer.Serialize(req)); await cmd.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
        return Results.Created($"/api/v1/vehicles/{vehicleId}", new { success=true, data=new { id=vehicleId, plate=req.Plate.Trim().ToUpperInvariant(), internalNumber=req.InternalNumber, currentMileage=req.CurrentMileage, qrToken=token } });
    }
    catch(PostgresException ex) when (ex.SqlState == "23505") { await tx.RollbackAsync(); return Results.Conflict(new {success=false,error=new{code="DUPLICATE_VEHICLE",message="La placa o número interno ya está registrado."}}); }
}).RequireAuthorization();

// PLANES
app.MapGet("/api/v1/maintenance-plans", async (ClaimsPrincipal principal) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    var companyId = CompanyId(principal);
    await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync();
    var sql = @"SELECT mp.id, mp.name, mp.brand, mp.model, mp.variant, mpv.id, mpv.version_number, mpv.status
                FROM maintenance_plans mp
                JOIN maintenance_plan_versions mpv ON mpv.maintenance_plan_id=mp.id
                WHERE mp.company_id=@c AND mp.status='ACTIVE' AND COALESCE(mp.is_vehicle_specific,false)=false
                ORDER BY mp.name, mpv.version_number DESC";
    await using var cmd = new NpgsqlCommand(sql, con); cmd.Parameters.AddWithValue("c", companyId);
    await using var r = await cmd.ExecuteReaderAsync();
    var list = new List<object>();
    while(await r.ReadAsync()) list.Add(new { id=r.GetGuid(0), name=r.GetString(1), brand=r.GetString(2), model=r.GetString(3), variant=r.IsDBNull(4)?null:r.GetString(4), versionId=r.GetGuid(5), versionNumber=r.GetInt32(6), status=r.GetString(7) });
    return Results.Ok(new { success=true, data=list });
}).RequireAuthorization();

app.MapPost("/api/v1/maintenance-plans", async (ClaimsPrincipal principal, CreatePlanRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Name)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
        return Results.BadRequest(new{success=false,error=new{message="Nombre, marca y modelo son obligatorios."}});
    var companyId=CompanyId(principal); var userId=UserId(principal);
    await using var con = new NpgsqlConnection(connectionString); await con.OpenAsync(); await using var tx=await con.BeginTransactionAsync();
    try
    {
        var planId=Guid.NewGuid(); var versionId=Guid.NewGuid();
        await using(var c1=new NpgsqlCommand("INSERT INTO maintenance_plans(id,company_id,name,brand,model,variant) VALUES(@id,@c,@n,@b,@m,@v)",con,tx))
        { c1.Parameters.AddWithValue("id",planId);c1.Parameters.AddWithValue("c",companyId);c1.Parameters.AddWithValue("n",req.Name.Trim());c1.Parameters.AddWithValue("b",req.Brand.Trim());c1.Parameters.AddWithValue("m",req.Model.Trim());c1.Parameters.AddWithValue("v",(object?)req.Variant?.Trim()??DBNull.Value);await c1.ExecuteNonQueryAsync();}
        await using(var c2=new NpgsqlCommand("INSERT INTO maintenance_plan_versions(id,maintenance_plan_id,version_number,status,published_at,created_by_user_id) VALUES(@id,@p,1,'PUBLISHED',now(),@u)",con,tx))
        {c2.Parameters.AddWithValue("id",versionId);c2.Parameters.AddWithValue("p",planId);c2.Parameters.AddWithValue("u",userId);await c2.ExecuteNonQueryAsync();}
        await tx.CommitAsync();
        return Results.Ok(new {success=true,data=new{id=planId,versionId,versionNumber=1}});
    }
    catch(PostgresException ex) when(ex.SqlState=="23505") { await tx.RollbackAsync(); return Results.Conflict(new{success=false,error=new{message="Ya existe un plan con ese nombre."}});}
}).RequireAuthorization();


app.MapPatch("/api/v1/maintenance-plans/{planId:guid}", async (ClaimsPrincipal principal, Guid planId, UpdatePlanRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Name)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
        return Results.BadRequest(new{success=false,error=new{message="Nombre, marca y modelo son obligatorios."}});
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    try{
      await using var cmd=new NpgsqlCommand("UPDATE maintenance_plans SET name=@n,brand=@b,model=@m WHERE id=@p AND company_id=@c",con);
      cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("b",ToTitleCase(req.Brand));cmd.Parameters.AddWithValue("m",ToTitleCase(req.Model));cmd.Parameters.AddWithValue("p",planId);cmd.Parameters.AddWithValue("c",CompanyId(principal));
      var n=await cmd.ExecuteNonQueryAsync();if(n==0)return Results.NotFound();
      return Results.Ok(new{success=true});
    }catch(PostgresException ex) when(ex.SqlState=="23505"){return Results.Conflict(new{success=false,error=new{message="Ya existe un plan con ese nombre."}});}
}).RequireAuthorization();

app.MapGet("/api/v1/plan-versions/{versionId:guid}/services", async (ClaimsPrincipal principal, Guid versionId) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    var companyId=CompanyId(principal);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    var sql=@"SELECT s.id,s.name,s.category,s.specification,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days
              FROM maintenance_plan_services s
              JOIN maintenance_plan_versions pv ON pv.id=s.plan_version_id
              JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id
              WHERE s.plan_version_id=@v AND p.company_id=@c AND s.active=true ORDER BY s.sort_order,s.name";
    await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",versionId);cmd.Parameters.AddWithValue("c",companyId);
    await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();
    while(await r.ReadAsync()) list.Add(ServiceObject(r));
    return Results.Ok(new{success=true,data=list});
}).RequireAuthorization();

app.MapPost("/api/v1/plan-versions/{versionId:guid}/services", async (ClaimsPrincipal principal, Guid versionId, CreateServiceRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
    if(string.IsNullOrWhiteSpace(req.Category))return Results.BadRequest(new{success=false,error=new{message="La categoría es obligatoria."}});
    if(req.IntervalKm is null && req.IntervalMonths is null) return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
    if(req.IntervalKm.HasValue && req.IntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
    if(req.PrealertKm.HasValue && req.PrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
    var companyId=CompanyId(principal);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    await using(var verify=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@v AND p.company_id=@c",con))
    {verify.Parameters.AddWithValue("v",versionId);verify.Parameters.AddWithValue("c",companyId);if(await verify.ExecuteScalarAsync() is null)return Results.NotFound();}
    var id=Guid.NewGuid();
    await using var cmd=new NpgsqlCommand(@"INSERT INTO maintenance_plan_services(id,plan_version_id,name,category,specification,interval_km,interval_months,prealert_km,prealert_days)
                                           VALUES(@id,@v,@n,@cat,@spec,@ikm,@imon,@pkm,@pday)",con);
    cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("v",versionId);cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("cat",req.Category.Trim());
    cmd.Parameters.AddWithValue("spec",(object?)req.Specification?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("ikm",(object?)req.IntervalKm??DBNull.Value);cmd.Parameters.AddWithValue("imon",(object?)req.IntervalMonths??DBNull.Value);cmd.Parameters.AddWithValue("pkm",(object?)req.PrealertKm??DBNull.Value);cmd.Parameters.AddWithValue("pday",(object?)req.PrealertDays??DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new{success=true,data=new{id}});
}).RequireAuthorization();


app.MapPatch("/api/v1/plan-services/{serviceId:guid}", async (ClaimsPrincipal principal, Guid serviceId, UpdateServiceRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
    if(req.IntervalKm is null && req.IntervalMonths is null)return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
    if(req.IntervalKm.HasValue && req.IntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
    if(req.PrealertKm.HasValue && req.PrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    var sql=@"UPDATE maintenance_plan_services s SET name=@n,specification=@spec,interval_km=@ikm,interval_months=@imon,prealert_km=@pkm,prealert_days=@pday
              FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id
              WHERE s.id=@id AND s.plan_version_id=pv.id AND p.company_id=@c AND s.active=true";
    await using var cmd=new NpgsqlCommand(sql,con);
    cmd.Parameters.AddWithValue("n",req.Name.Trim());cmd.Parameters.AddWithValue("spec",(object?)req.Specification?.Trim()??DBNull.Value);
    cmd.Parameters.AddWithValue("ikm",(object?)req.IntervalKm??DBNull.Value);cmd.Parameters.AddWithValue("imon",(object?)req.IntervalMonths??DBNull.Value);
    cmd.Parameters.AddWithValue("pkm",(object?)req.PrealertKm??DBNull.Value);cmd.Parameters.AddWithValue("pday",(object?)req.PrealertDays??DBNull.Value);
    cmd.Parameters.AddWithValue("id",serviceId);cmd.Parameters.AddWithValue("c",CompanyId(principal));
    if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();
    return Results.Ok(new{success=true});
}).RequireAuthorization();

app.MapPost("/api/v1/vehicles/{vehicleId:guid}/assign-plan", async (ClaimsPrincipal principal, Guid vehicleId, AssignPlanRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    var companyId=CompanyId(principal); var userId=UserId(principal);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
    var check=@"SELECT 1 FROM vehicles v CROSS JOIN maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE v.id=@vehicle AND v.company_id=@c AND pv.id=@version AND p.company_id=@c";
    await using(var c=new NpgsqlCommand(check,con,tx)){c.Parameters.AddWithValue("vehicle",vehicleId);c.Parameters.AddWithValue("version",req.PlanVersionId);c.Parameters.AddWithValue("c",companyId);if(await c.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.NotFound();}}
    await using(var close=new NpgsqlCommand("UPDATE vehicle_plan_assignments SET active=false,ends_at=now() WHERE vehicle_id=@v AND active=true",con,tx)){close.Parameters.AddWithValue("v",vehicleId);await close.ExecuteNonQueryAsync();}
    await using(var ins=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx)){ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("p",req.PlanVersionId);ins.Parameters.AddWithValue("u",userId);await ins.ExecuteNonQueryAsync();}
    await tx.CommitAsync();return Results.Ok(new{success=true});
}).RequireAuthorization();

app.MapPost("/api/v1/vehicles/{vehicleId:guid}/baselines", async (ClaimsPrincipal principal, Guid vehicleId, BaselineRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(req.LastServiceMileage.HasValue && req.LastServiceMileage.Value<0)
        return Results.BadRequest(new{success=false,error=new{message="El kilometraje no puede ser negativo."}});
    var companyId=CompanyId(principal);var userId=UserId(principal);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    var verify=@"SELECT 1 FROM vehicles v JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id WHERE v.id=@v AND v.company_id=@c AND s.id=@s";
    await using(var c=new NpgsqlCommand(verify,con)){c.Parameters.AddWithValue("v",vehicleId);c.Parameters.AddWithValue("c",companyId);c.Parameters.AddWithValue("s",req.PlanServiceId);if(await c.ExecuteScalarAsync() is null)return Results.BadRequest(new{success=false,error=new{message="El servicio no pertenece al plan activo del vehículo."}});}
    var sql=@"INSERT INTO vehicle_service_baselines(vehicle_id,plan_service_id,last_service_mileage,last_service_date,source,created_by_user_id)
              VALUES(@v,@s,@km,@d,'ADMIN',@u)
              ON CONFLICT(vehicle_id,plan_service_id) DO UPDATE SET last_service_mileage=excluded.last_service_mileage,last_service_date=excluded.last_service_date,created_by_user_id=excluded.created_by_user_id,created_at=now()";
    await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("s",req.PlanServiceId);cmd.Parameters.AddWithValue("km",(object?)req.LastServiceMileage??DBNull.Value);cmd.Parameters.AddWithValue("d",(object?)req.LastServiceDate?.Date??DBNull.Value);cmd.Parameters.AddWithValue("u",userId);await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new{success=true});
}).RequireAuthorization();

app.MapGet("/api/v1/vehicles/{vehicleId:guid}/maintenance-status", async (ClaimsPrincipal principal, Guid vehicleId) =>
{
    if(!CanAccessVehicle(principal,vehicleId))return Results.Forbid();
    var companyId=CompanyId(principal);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    int currentMileage; string plate; bool individualServices=false;
    await using(var vc=new NpgsqlCommand(@"SELECT v.current_mileage,v.plate,COALESCE(mp.is_vehicle_specific,false)
        FROM vehicles v
        LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
        LEFT JOIN maintenance_plan_versions pv ON pv.id=a.plan_version_id
        LEFT JOIN maintenance_plans mp ON mp.id=pv.maintenance_plan_id
        WHERE v.id=@v AND v.company_id=@c",con)){vc.Parameters.AddWithValue("v",vehicleId);vc.Parameters.AddWithValue("c",companyId);await using var vr=await vc.ExecuteReaderAsync();if(!await vr.ReadAsync())return Results.NotFound();currentMileage=vr.GetInt32(0);plate=vr.GetString(1);individualServices=vr.GetBoolean(2);}
    var sql=@"SELECT s.id,s.name,s.category,s.specification,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days,b.last_service_mileage,b.last_service_date
              FROM vehicle_plan_assignments a
              JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id AND s.active=true
              LEFT JOIN vehicle_service_baselines b ON b.vehicle_id=a.vehicle_id AND b.plan_service_id=s.id
              WHERE a.vehicle_id=@v AND a.active=true ORDER BY s.sort_order,s.name";
    await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);await using var r=await cmd.ExecuteReaderAsync();
    var items=new List<MaintenanceStatusItem>();
    while(await r.ReadAsync())
    {
        var serviceId=r.GetGuid(0);var name=r.GetString(1);var intervalKm=r.IsDBNull(4)?(int?)null:r.GetInt32(4);var intervalMonths=r.IsDBNull(5)?(int?)null:r.GetInt32(5);var prealertKm=r.IsDBNull(6)?0:r.GetInt32(6);var prealertDays=r.IsDBNull(7)?0:r.GetInt32(7);var lastKm=r.IsDBNull(8)?(int?)null:r.GetInt32(8);var lastDate=r.IsDBNull(9)?(DateTime?)null:r.GetDateTime(9);
        items.Add(CalcStatus(serviceId,name,currentMileage,intervalKm,intervalMonths,prealertKm,prealertDays,lastKm,lastDate));
    }
    if(items.Count==0) return Results.Ok(new{success=true,data=new{plate,currentMileage,overallStatus="NO_PLAN",hasIncompleteHistory=false,individualServices,services=items}});
    var overall=items.Any(x=>x.Status=="OVERDUE")?"OVERDUE":items.Any(x=>x.Status=="DUE_SOON")?"DUE_SOON":items.All(x=>x.Status=="NO_BASELINE")?"NO_BASELINE":"UP_TO_DATE";
    var incomplete=items.Any(x=>x.Status=="NO_BASELINE");
    return Results.Ok(new{success=true,data=new{plate,currentMileage,overallStatus=overall,hasIncompleteHistory=incomplete,individualServices,services=items}});
}).RequireAuthorization();



// SERVICIOS INDIVIDUALES POR VEHÍCULO V31.3
app.MapPost("/api/v1/vehicles/{vehicleId:guid}/individual-services", async (ClaimsPrincipal principal, Guid vehicleId, CreateServiceRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Name))return Results.BadRequest(new{success=false,error=new{message="El nombre del servicio es obligatorio."}});
    if(req.IntervalKm is null && req.IntervalMonths is null)return Results.BadRequest(new{success=false,error=new{message="Debes definir un intervalo por kilometraje o por tiempo."}});
    if(req.IntervalKm.HasValue && req.IntervalKm.Value<=0)return Results.BadRequest(new{success=false,error=new{message="El intervalo por kilometraje debe ser mayor que cero."}});
    if(req.PrealertKm.HasValue && req.PrealertKm.Value<0)return Results.BadRequest(new{success=false,error=new{message="La prealerta no puede ser negativa."}});
    var companyId=CompanyId(principal);var userId=UserId(principal);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
    try
    {
        string brand,model;Guid? activeVersion=null;bool activeIsIndividual=false;
        await using(var vc=new NpgsqlCommand(@"SELECT v.brand,v.model,a.plan_version_id,COALESCE(mp.is_vehicle_specific,false)
          FROM vehicles v
          LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
          LEFT JOIN maintenance_plan_versions pv ON pv.id=a.plan_version_id
          LEFT JOIN maintenance_plans mp ON mp.id=pv.maintenance_plan_id
          WHERE v.id=@v AND v.company_id=@c FOR UPDATE OF v",con,tx))
        {
            vc.Parameters.AddWithValue("v",vehicleId);vc.Parameters.AddWithValue("c",companyId);
            await using var r=await vc.ExecuteReaderAsync();
            if(!await r.ReadAsync()){await tx.RollbackAsync();return Results.NotFound();}
            brand=r.GetString(0);model=r.GetString(1);activeVersion=r.IsDBNull(2)?null:r.GetGuid(2);activeIsIndividual=r.GetBoolean(3);
        }
        if(activeVersion.HasValue && !activeIsIndividual){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="Este vehículo ya usa un Plan de Mantenimiento. Edita los servicios desde el plan."}});}
        Guid versionId;
        if(activeVersion.HasValue) versionId=activeVersion.Value;
        else
        {
            var planId=Guid.NewGuid();versionId=Guid.NewGuid();
            await using(var p=new NpgsqlCommand(@"INSERT INTO maintenance_plans(id,company_id,name,brand,model,status,is_vehicle_specific,vehicle_id)
              VALUES(@id,@c,@n,@b,@m,'ACTIVE',true,@v)",con,tx))
            {p.Parameters.AddWithValue("id",planId);p.Parameters.AddWithValue("c",companyId);p.Parameters.AddWithValue("n",$"__VEHICLE_{vehicleId:N}");p.Parameters.AddWithValue("b",brand);p.Parameters.AddWithValue("m",model);p.Parameters.AddWithValue("v",vehicleId);await p.ExecuteNonQueryAsync();}
            await using(var pv=new NpgsqlCommand(@"INSERT INTO maintenance_plan_versions(id,maintenance_plan_id,version_number,status,published_at,created_by_user_id)
              VALUES(@id,@p,1,'PUBLISHED',now(),@u)",con,tx))
            {pv.Parameters.AddWithValue("id",versionId);pv.Parameters.AddWithValue("p",planId);pv.Parameters.AddWithValue("u",userId);await pv.ExecuteNonQueryAsync();}
            await using(var a=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx))
            {a.Parameters.AddWithValue("v",vehicleId);a.Parameters.AddWithValue("p",versionId);a.Parameters.AddWithValue("u",userId);await a.ExecuteNonQueryAsync();}
        }
        var serviceId=Guid.NewGuid();
        await using(var sc=new NpgsqlCommand(@"INSERT INTO maintenance_plan_services(id,plan_version_id,name,category,specification,interval_km,interval_months,prealert_km,prealert_days)
          VALUES(@id,@pv,@n,@cat,@sp,@ik,@im,@pk,@pd)",con,tx))
        {
            sc.Parameters.AddWithValue("id",serviceId);sc.Parameters.AddWithValue("pv",versionId);sc.Parameters.AddWithValue("n",req.Name.Trim());
            sc.Parameters.AddWithValue("cat",string.IsNullOrWhiteSpace(req.Category)?"General":req.Category.Trim());
            sc.Parameters.AddWithValue("sp",(object?)req.Specification?.Trim()??DBNull.Value);sc.Parameters.AddWithValue("ik",(object?)req.IntervalKm??DBNull.Value);
            sc.Parameters.AddWithValue("im",(object?)req.IntervalMonths??DBNull.Value);sc.Parameters.AddWithValue("pk",(object?)req.PrealertKm??DBNull.Value);
            sc.Parameters.AddWithValue("pd",(object?)req.PrealertDays??DBNull.Value);await sc.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();return Results.Ok(new{success=true,data=new{id=serviceId}});
    }
    catch(Exception ex){await tx.RollbackAsync();app.Logger.LogError(ex,"Create individual vehicle service failed");return Results.Json(new{success=false,error=new{message="No fue posible crear el servicio del vehículo."}},statusCode:500);}
}).RequireAuthorization();

// CENTRO DE CONTROL + PREDICCIÓN V8
app.MapGet("/api/v1/dashboard", async (ClaimsPrincipal principal) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    var companyId=CompanyId(principal);
    await using var con=new NpgsqlConnection(connectionString); await con.OpenAsync();

    var sql=@"SELECT v.id,v.plate,v.internal_number,v.brand,v.model,v.variant,v.current_mileage,
                     s.id,s.name,s.interval_km,s.interval_months,s.prealert_km,s.prealert_days,
                     b.last_service_mileage,b.last_service_date
              FROM vehicles v
              LEFT JOIN vehicle_plan_assignments a ON a.vehicle_id=v.id AND a.active=true
              LEFT JOIN maintenance_plan_services s ON s.plan_version_id=a.plan_version_id AND s.active=true
              LEFT JOIN vehicle_service_baselines b ON b.vehicle_id=v.id AND b.plan_service_id=s.id
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
            string name; string? spec; int? intervalKm; int? intervalMonths;
            await using(var sc=new NpgsqlCommand(@"SELECT s.name,s.specification,s.interval_km,s.interval_months FROM maintenance_plan_services s JOIN vehicle_plan_assignments a ON a.plan_version_id=s.plan_version_id AND a.active=true WHERE a.vehicle_id=@v AND s.id=@s AND s.active=true",con,tx))
            {sc.Parameters.AddWithValue("v",vehicleId);sc.Parameters.AddWithValue("s",serviceId);await using var r=await sc.ExecuteReaderAsync();if(!await r.ReadAsync()){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{code="INVALID_SERVICE",message="Uno de los servicios no pertenece al plan activo del vehículo."}});}name=r.GetString(0);spec=r.IsDBNull(1)?null:r.GetString(1);intervalKm=r.IsDBNull(2)?null:r.GetInt32(2);intervalMonths=r.IsDBNull(3)?null:r.GetInt32(3);}
            int? nextKm=intervalKm.HasValue?req.Mileage+intervalKm.Value:null; DateTime? nextDate=intervalMonths.HasValue?req.ServiceDate.Date.AddMonths(intervalMonths.Value):null;
            await using(var it=new NpgsqlCommand(@"INSERT INTO maintenance_record_items(maintenance_record_id,plan_service_id,service_name_snapshot,specification_snapshot,interval_km_snapshot,interval_months_snapshot,next_due_mileage,next_due_date) VALUES(@r,@s,@n,@sp,@ik,@im,@nk,@nd)",con,tx))
            {it.Parameters.AddWithValue("r",recordId);it.Parameters.AddWithValue("s",serviceId);it.Parameters.AddWithValue("n",name);it.Parameters.AddWithValue("sp",(object?)spec??DBNull.Value);it.Parameters.AddWithValue("ik",(object?)intervalKm??DBNull.Value);it.Parameters.AddWithValue("im",(object?)intervalMonths??DBNull.Value);it.Parameters.AddWithValue("nk",(object?)nextKm??DBNull.Value);it.Parameters.AddWithValue("nd",(object?)nextDate?.Date??DBNull.Value);await it.ExecuteNonQueryAsync();}
            await using(var b=new NpgsqlCommand(@"INSERT INTO vehicle_service_baselines(vehicle_id,plan_service_id,last_service_mileage,last_service_date,source,created_by_user_id) VALUES(@v,@s,@km,@d,'MAINTENANCE',@u) ON CONFLICT(vehicle_id,plan_service_id) DO UPDATE SET last_service_mileage=excluded.last_service_mileage,last_service_date=excluded.last_service_date,source='MAINTENANCE',created_by_user_id=excluded.created_by_user_id,created_at=now()",con,tx))
            {b.Parameters.AddWithValue("v",vehicleId);b.Parameters.AddWithValue("s",serviceId);b.Parameters.AddWithValue("km",req.Mileage);b.Parameters.AddWithValue("d",req.ServiceDate.Date);b.Parameters.AddWithValue("u",userId);await b.ExecuteNonQueryAsync();}
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

app.MapGet("/api/v1/vehicles/{vehicleId:guid}/qr-label", async (ClaimsPrincipal principal,Guid vehicleId) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
    var sql=@"SELECT v.plate,v.internal_number,v.brand,v.model,v.variant,q.token,c.name,c.logo_data_url
      FROM vehicles v JOIN vehicle_qr_tokens q ON q.vehicle_id=v.id AND q.status='ACTIVE'
      JOIN companies c ON c.id=v.company_id WHERE v.id=@v AND v.company_id=@c AND v.status='ACTIVE' LIMIT 1";
    await using var cmd=new NpgsqlCommand(sql,con);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));
    await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound();
    return Results.Ok(new{success=true,data=new{plate=r.GetString(0),internalNumber=r.IsDBNull(1)?null:r.GetString(1),brand=r.GetString(2),model=r.GetString(3),variant=r.IsDBNull(4)?null:r.GetString(4),qrToken=r.GetString(5),companyName=r.GetString(6),logoDataUrl=r.IsDBNull(7)?null:r.GetString(7)}});
}).RequireAuthorization();

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

app.MapPatch("/api/v1/vehicles/{vehicleId:guid}", async (ClaimsPrincipal principal, Guid vehicleId, UpdateVehicleRequest req) =>
{
    if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
    if(string.IsNullOrWhiteSpace(req.Plate)||string.IsNullOrWhiteSpace(req.Brand)||string.IsNullOrWhiteSpace(req.Model))
        return Results.BadRequest(new{success=false,error=new{message="Placa, marca y modelo son obligatorios."}});
    var plate=req.Plate.Trim().ToUpperInvariant();
    var internalNumber=string.IsNullOrWhiteSpace(req.InternalNumber)?null:req.InternalNumber.Trim().ToUpperInvariant();
    var brand=ToTitleCase(req.Brand);var model=ToTitleCase(req.Model);
    await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
    try{
      await using(var planCheck=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@pv AND p.company_id=@c AND pv.status='PUBLISHED'",con,tx))
      {planCheck.Parameters.AddWithValue("pv",req.PlanVersionId);planCheck.Parameters.AddWithValue("c",CompanyId(principal));if(await planCheck.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="Selecciona un plan de mantenimiento válido."}});}}
      await using(var cmd=new NpgsqlCommand(@"UPDATE vehicles SET plate=@p,internal_number=@i,brand=@b,model=@m WHERE id=@v AND company_id=@c AND status='ACTIVE'",con,tx))
      {cmd.Parameters.AddWithValue("p",plate);cmd.Parameters.AddWithValue("i",(object?)internalNumber??DBNull.Value);cmd.Parameters.AddWithValue("b",brand);cmd.Parameters.AddWithValue("m",model);cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
      Guid? currentPlan=null;
      await using(var cur=new NpgsqlCommand("SELECT plan_version_id FROM vehicle_plan_assignments WHERE vehicle_id=@v AND active=true LIMIT 1",con,tx))
      {cur.Parameters.AddWithValue("v",vehicleId);var x=await cur.ExecuteScalarAsync();if(x is Guid g)currentPlan=g;}
      if(currentPlan!=req.PlanVersionId)
      {
        await using(var close=new NpgsqlCommand("UPDATE vehicle_plan_assignments SET active=false,ends_at=now() WHERE vehicle_id=@v AND active=true",con,tx)){close.Parameters.AddWithValue("v",vehicleId);await close.ExecuteNonQueryAsync();}
        await using(var ins=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx)){ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("p",req.PlanVersionId);ins.Parameters.AddWithValue("u",UserId(principal));await ins.ExecuteNonQueryAsync();}
      }
      await tx.CommitAsync();return Results.Ok(new{success=true});
    }catch(PostgresException ex) when(ex.SqlState=="23505"){await tx.RollbackAsync();return Results.Conflict(new{success=false,error=new{message="La placa o número interno ya está registrado."}});}
}).RequireAuthorization();



app.MapGet("/api/v1/vehicles/archived", async (ClaimsPrincipal principal) =>
{
 if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
 await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
 await using var cmd=new NpgsqlCommand(@"SELECT id,plate,internal_number,brand,model,current_mileage FROM vehicles WHERE company_id=@c AND status='ARCHIVED' ORDER BY internal_number NULLS LAST,plate",con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
 await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),plate=r.GetString(1),internalNumber=r.IsDBNull(2)?null:r.GetString(2),brand=r.GetString(3),model=r.GetString(4),currentMileage=r.GetInt32(5)});
 return Results.Ok(new{success=true,data=list});
}).RequireAuthorization();

app.MapPatch("/api/v1/vehicles/{vehicleId:guid}/reactivate", async (ClaimsPrincipal principal,Guid vehicleId,ReactivateVehicleRequest req) =>
{
 if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
 await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
 await using(var chk=new NpgsqlCommand(@"SELECT 1 FROM maintenance_plan_versions pv JOIN maintenance_plans p ON p.id=pv.maintenance_plan_id WHERE pv.id=@p AND p.company_id=@c AND p.status='ACTIVE' AND pv.status='PUBLISHED'",con,tx)){chk.Parameters.AddWithValue("p",req.PlanVersionId);chk.Parameters.AddWithValue("c",CompanyId(principal));if(await chk.ExecuteScalarAsync() is null){await tx.RollbackAsync();return Results.BadRequest(new{success=false,error=new{message="Selecciona un plan activo válido."}});}}
 await using(var cmd=new NpgsqlCommand("UPDATE vehicles SET status='ACTIVE' WHERE id=@v AND company_id=@c AND status='ARCHIVED'",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
 await using(var ins=new NpgsqlCommand("INSERT INTO vehicle_plan_assignments(vehicle_id,plan_version_id,assigned_by_user_id) VALUES(@v,@p,@u)",con,tx)){ins.Parameters.AddWithValue("v",vehicleId);ins.Parameters.AddWithValue("p",req.PlanVersionId);ins.Parameters.AddWithValue("u",UserId(principal));await ins.ExecuteNonQueryAsync();}
 var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
 await using(var qr=new NpgsqlCommand("INSERT INTO vehicle_qr_tokens(vehicle_id,token,status) VALUES(@v,@t,'ACTIVE')",con,tx)){qr.Parameters.AddWithValue("v",vehicleId);qr.Parameters.AddWithValue("t",token);await qr.ExecuteNonQueryAsync();}
 await tx.CommitAsync();return Results.Ok(new{success=true});
}).RequireAuthorization();

app.MapGet("/api/v1/maintenance-plans/archived", async (ClaimsPrincipal principal) =>
{
 if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
 await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
 await using var cmd=new NpgsqlCommand("SELECT id,name,brand,model FROM maintenance_plans WHERE company_id=@c AND status='ARCHIVED' ORDER BY name",con);cmd.Parameters.AddWithValue("c",CompanyId(principal));
 await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),name=r.GetString(1),brand=r.GetString(2),model=r.GetString(3)});
 return Results.Ok(new{success=true,data=list});
}).RequireAuthorization();

app.MapPatch("/api/v1/maintenance-plans/{planId:guid}/reactivate", async (ClaimsPrincipal principal,Guid planId) =>
{
 if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
 await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var cmd=new NpgsqlCommand("UPDATE maintenance_plans SET status='ACTIVE' WHERE id=@p AND company_id=@c AND status='ARCHIVED'",con);cmd.Parameters.AddWithValue("p",planId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();return Results.Ok(new{success=true});
}).RequireAuthorization();

app.MapPatch("/api/v1/vehicles/{vehicleId:guid}/archive", async (ClaimsPrincipal principal,Guid vehicleId) =>
{
 if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
 await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();await using var tx=await con.BeginTransactionAsync();
 await using(var cmd=new NpgsqlCommand("UPDATE vehicles SET status='ARCHIVED' WHERE id=@v AND company_id=@c AND status='ACTIVE'",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0){await tx.RollbackAsync();return Results.NotFound();}}
 await using(var cmd=new NpgsqlCommand("UPDATE vehicle_plan_assignments SET active=false,ends_at=now() WHERE vehicle_id=@v AND active=true",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);await cmd.ExecuteNonQueryAsync();}
 await using(var cmd=new NpgsqlCommand("UPDATE vehicle_qr_tokens SET status='REVOKED' WHERE vehicle_id=@v AND status='ACTIVE'",con,tx)){cmd.Parameters.AddWithValue("v",vehicleId);await cmd.ExecuteNonQueryAsync();}
 await tx.CommitAsync();return Results.Ok(new{success=true});
}).RequireAuthorization();

app.MapPatch("/api/v1/maintenance-plans/{planId:guid}/archive", async (ClaimsPrincipal principal,Guid planId) =>
{
 if(principal.FindFirstValue("role")!="COMPANY_ADMIN")return Results.Forbid();
 await using var con=new NpgsqlConnection(connectionString);await con.OpenAsync();
 await using(var chk=new NpgsqlCommand(@"SELECT count(*) FROM vehicle_plan_assignments a JOIN vehicles v ON v.id=a.vehicle_id WHERE a.active=true AND v.status='ACTIVE' AND v.company_id=@c AND a.plan_version_id IN (SELECT id FROM maintenance_plan_versions WHERE maintenance_plan_id=@p)",con)){chk.Parameters.AddWithValue("p",planId);chk.Parameters.AddWithValue("c",CompanyId(principal));var count=Convert.ToInt32(await chk.ExecuteScalarAsync());if(count>0)return Results.Conflict(new{success=false,error=new{message=$"Este plan está asignado a {count} vehículo(s) activo(s). Cambia primero esos vehículos a otro plan."}});}
 await using var cmd=new NpgsqlCommand("UPDATE maintenance_plans SET status='ARCHIVED' WHERE id=@p AND company_id=@c AND status='ACTIVE'",con);cmd.Parameters.AddWithValue("p",planId);cmd.Parameters.AddWithValue("c",CompanyId(principal));if(await cmd.ExecuteNonQueryAsync()==0)return Results.NotFound();
 return Results.Ok(new{success=true});
}).RequireAuthorization();

app.Run();


static string ToTitleCase(string? value)
{
    if(string.IsNullOrWhiteSpace(value))return "";
    var ti=System.Globalization.CultureInfo.GetCultureInfo("es-CO").TextInfo;
    return ti.ToTitleCase(value.Trim().ToLowerInvariant());
}

static string CreateJwt(DemoUser user,string jwtKey,Guid? workVehicleId=null)
{
    var claims=new List<Claim>
    {
        new Claim("user_id",user.Id.ToString()),
        new Claim("company_id",user.CompanyId.ToString()),
        new Claim("name",user.FullName),
        new Claim("role",user.Role)
    };
    if(workVehicleId.HasValue)claims.Add(new Claim("work_vehicle_id",workVehicleId.Value.ToString()));
    var token=new JwtSecurityToken(claims:claims,expires:DateTime.UtcNow.AddHours(8),
        signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}
static bool CanAccessVehicle(ClaimsPrincipal p,Guid vehicleId)
{
    if(p.FindFirstValue("role")=="COMPANY_ADMIN")return true;
    if(p.FindFirstValue("role")!="TECHNICIAN")return false;
    var x=p.FindFirstValue("work_vehicle_id");
    return Guid.TryParse(x,out var id)&&id==vehicleId;
}

static Guid CompanyId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("company_id")!);
static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("user_id")!);
static object ServiceObject(NpgsqlDataReader r) => new {id=r.GetGuid(0),name=r.GetString(1),category=r.GetString(2),specification=r.IsDBNull(3)?null:r.GetString(3),intervalKm=r.IsDBNull(4)?(int?)null:r.GetInt32(4),intervalMonths=r.IsDBNull(5)?(int?)null:r.GetInt32(5),prealertKm=r.IsDBNull(6)?(int?)null:r.GetInt32(6),prealertDays=r.IsDBNull(7)?(int?)null:r.GetInt32(7)};
static MaintenanceStatusItem CalcStatus(Guid id,string name,int currentMileage,int? intervalKm,int? intervalMonths,int prealertKm,int prealertDays,int? lastKm,DateTime? lastDate)
{
    if(lastKm is null && lastDate is null) return new(id,name,"NO_BASELINE",null,null,null,null);
    int? nextKm = intervalKm.HasValue && lastKm.HasValue ? lastKm.Value + intervalKm.Value : null;
    DateTime? nextDate = intervalMonths.HasValue && lastDate.HasValue ? lastDate.Value.Date.AddMonths(intervalMonths.Value) : null;
    int? remainingKm = nextKm.HasValue ? nextKm.Value-currentMileage : null;
    int? remainingDays = nextDate.HasValue ? (nextDate.Value.Date-DateTime.UtcNow.Date).Days : null;
    var overdue=(remainingKm.HasValue && remainingKm.Value<=0)||(remainingDays.HasValue && remainingDays.Value<=0);
    var soon=!overdue && ((remainingKm.HasValue && remainingKm.Value<=prealertKm)||(remainingDays.HasValue && remainingDays.Value<=prealertDays));
    return new(id,name,overdue?"OVERDUE":soon?"DUE_SOON":"UP_TO_DATE",nextKm,nextDate,remainingKm,remainingDays);
}

static async Task EnsureDatabaseReady(string cs)
{
    Exception? last=null;
    for(var i=1;i<=30;i++)
    {
        try{await using var con=new NpgsqlConnection(cs);await con.OpenAsync();Console.WriteLine($"Database connection ready on attempt {i}.");return;}
        catch(Exception ex){last=ex;Console.WriteLine($"Database not ready (attempt {i}/30): {ex.Message}");await Task.Delay(2000);}
    }
    throw new InvalidOperationException("Database did not become available.",last);
}

static async Task EnsureV5Schema(string cs)
{
    var sql=@"
CREATE TABLE IF NOT EXISTS maintenance_plans (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id), name varchar(150) NOT NULL,
 brand varchar(100) NOT NULL, model varchar(100) NOT NULL, variant varchar(100), status varchar(20) NOT NULL DEFAULT 'ACTIVE', created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), UNIQUE(company_id,name));
CREATE TABLE IF NOT EXISTS maintenance_plan_versions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), maintenance_plan_id uuid NOT NULL REFERENCES maintenance_plans(id), version_number integer NOT NULL,
 status varchar(20) NOT NULL DEFAULT 'PUBLISHED', published_at timestamptz, created_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(maintenance_plan_id,version_number));
CREATE TABLE IF NOT EXISTS maintenance_plan_services (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), plan_version_id uuid NOT NULL REFERENCES maintenance_plan_versions(id), name varchar(150) NOT NULL, category varchar(100) NOT NULL,
 specification varchar(300), interval_km integer, interval_months integer, prealert_km integer, prealert_days integer, active boolean NOT NULL DEFAULT true, sort_order integer NOT NULL DEFAULT 0, created_at timestamptz NOT NULL DEFAULT now(), CHECK(interval_km IS NOT NULL OR interval_months IS NOT NULL));
CREATE TABLE IF NOT EXISTS vehicle_plan_assignments (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), vehicle_id uuid NOT NULL REFERENCES vehicles(id), plan_version_id uuid NOT NULL REFERENCES maintenance_plan_versions(id), starts_at timestamptz NOT NULL DEFAULT now(), ends_at timestamptz, active boolean NOT NULL DEFAULT true, assigned_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now());
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_active_plan ON vehicle_plan_assignments(vehicle_id) WHERE active=true;
CREATE TABLE IF NOT EXISTS vehicle_service_baselines (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), vehicle_id uuid NOT NULL REFERENCES vehicles(id), plan_service_id uuid NOT NULL REFERENCES maintenance_plan_services(id), last_service_mileage integer, last_service_date date, source varchar(30) NOT NULL DEFAULT 'ADMIN', created_by_user_id uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(vehicle_id,plan_service_id));";
    await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
}

static async Task EnsureV6Schema(string cs)
{
    var sql=@"
CREATE TABLE IF NOT EXISTS maintenance_records (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id), vehicle_id uuid NOT NULL REFERENCES vehicles(id), technician_user_id uuid REFERENCES users(id), service_date date NOT NULL, mileage integer NOT NULL, notes text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS maintenance_record_items (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), maintenance_record_id uuid NOT NULL REFERENCES maintenance_records(id), plan_service_id uuid NOT NULL REFERENCES maintenance_plan_services(id), service_name_snapshot varchar(150) NOT NULL, specification_snapshot varchar(300), interval_km_snapshot integer, interval_months_snapshot integer, next_due_mileage integer, next_due_date date, created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS ix_maintenance_records_vehicle_date ON maintenance_records(vehicle_id,service_date DESC);
CREATE INDEX IF NOT EXISTS ix_maintenance_items_record ON maintenance_record_items(maintenance_record_id);";
    await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
}


static async Task EnsureV12Schema(string cs)
{
    var sql=@"
ALTER TABLE companies ADD COLUMN IF NOT EXISTS code varchar(40);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS status varchar(20) NOT NULL DEFAULT 'ACTIVE';
ALTER TABLE companies ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
CREATE UNIQUE INDEX IF NOT EXISTS uq_companies_code ON companies(lower(code)) WHERE code IS NOT NULL;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS legal_name varchar(180);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS tax_id varchar(80);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS phone varchar(80);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS email varchar(180);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS address varchar(250);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS logo_data_url text;
CREATE SEQUENCE IF NOT EXISTS company_code_seq START 1;
DO $$
DECLARE mx bigint;
BEGIN
 SELECT COALESCE(MAX(NULLIF(regexp_replace(code,'[^0-9]','','g'), '')::bigint),0) INTO mx FROM companies WHERE code LIKE 'EMP-%';
 IF mx >= (SELECT last_value FROM company_code_seq) THEN PERFORM setval('company_code_seq',mx+1,false); END IF;
END $$;
";
    await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
}


static async Task EnsureV30Schema(string cs)
{
    var sql=@"
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_enabled boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_internal boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_email_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_email varchar(180);
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_due_soon boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_overdue boolean NOT NULL DEFAULT true;
ALTER TABLE companies ADD COLUMN IF NOT EXISTS notification_repeat_days integer NOT NULL DEFAULT 7;
CREATE TABLE IF NOT EXISTS notification_log(
 id uuid PRIMARY KEY,company_id uuid NOT NULL REFERENCES companies(id),vehicle_id uuid NOT NULL REFERENCES vehicles(id),
 plan_service_id uuid REFERENCES maintenance_plan_services(id),status varchar(30) NOT NULL,channel varchar(30) NOT NULL,
 recipient varchar(180),result varchar(30) NOT NULL DEFAULT 'RECORDED',created_by_user_id uuid REFERENCES users(id),
 created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS ix_notification_log_company_created ON notification_log(company_id,created_at DESC);
";
    await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
}


static async Task EnsureV31IndividualServicesSchema(string cs)
{
    var sql=@"
ALTER TABLE maintenance_plans ADD COLUMN IF NOT EXISTS is_vehicle_specific boolean NOT NULL DEFAULT false;
ALTER TABLE maintenance_plans ADD COLUMN IF NOT EXISTS vehicle_id uuid REFERENCES vehicles(id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_specific_plan ON maintenance_plans(vehicle_id) WHERE is_vehicle_specific=true AND vehicle_id IS NOT NULL;
";
    await using var con=new NpgsqlConnection(cs);await con.OpenAsync();await using var cmd=new NpgsqlCommand(sql,con);await cmd.ExecuteNonQueryAsync();
}

static async Task EnsureDemoData(string cs)
{
    await using var con=new NpgsqlConnection(cs);await con.OpenAsync();
    Guid companyId;
    await using(var cmd=new NpgsqlCommand("SELECT id FROM companies WHERE name='Empresa Demo' LIMIT 1",con))
    {var x=await cmd.ExecuteScalarAsync();if(x is Guid g) companyId=g;else{companyId=Guid.NewGuid();await using var ins=new NpgsqlCommand("INSERT INTO companies(id,name,code,status) VALUES(@id,'Empresa Demo','DEMO','ACTIVE')",con);ins.Parameters.AddWithValue("id",companyId);await ins.ExecuteNonQueryAsync();}}
    await using(var cmd=new NpgsqlCommand("SELECT 1 FROM users WHERE email='admin@demo.local'",con))
    {if(await cmd.ExecuteScalarAsync() is null){var id=Guid.NewGuid();var demo=new DemoUser(id,companyId,"Administrador Demo","admin@demo.local","","COMPANY_ADMIN");var temp=Environment.GetEnvironmentVariable("AUTOCONTROLQR_DEMO_PASSWORD")??Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));Console.WriteLine($"[INITIAL CREDENTIAL] admin@demo.local password: {temp}");var hash=new PasswordHasher<DemoUser>().HashPassword(demo,temp);await using var ins=new NpgsqlCommand("INSERT INTO users(id,company_id,full_name,email,password_hash,role) VALUES(@id,@c,@n,@e,@p,@r)",con);ins.Parameters.AddWithValue("id",id);ins.Parameters.AddWithValue("c",companyId);ins.Parameters.AddWithValue("n",demo.FullName);ins.Parameters.AddWithValue("e",demo.Email);ins.Parameters.AddWithValue("p",hash);ins.Parameters.AddWithValue("r",demo.Role);await ins.ExecuteNonQueryAsync();}}
    await using(var cmd=new NpgsqlCommand("SELECT 1 FROM users WHERE email='platform@autocontrol.local'",con))
    {if(await cmd.ExecuteScalarAsync() is null){var id=Guid.NewGuid();var admin=new DemoUser(id,companyId,"Administrador Plataforma","platform@autocontrol.local","","PLATFORM_ADMIN");var temp=Environment.GetEnvironmentVariable("AUTOCONTROLQR_PLATFORM_PASSWORD")??Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));Console.WriteLine($"[INITIAL CREDENTIAL] platform@autocontrol.local password: {temp}");var hash=new PasswordHasher<DemoUser>().HashPassword(admin,temp);await using var ins=new NpgsqlCommand("INSERT INTO users(id,company_id,full_name,email,password_hash,role,status) VALUES(@id,@c,@n,@e,@p,@r,'ACTIVE')",con);ins.Parameters.AddWithValue("id",id);ins.Parameters.AddWithValue("c",companyId);ins.Parameters.AddWithValue("n",admin.FullName);ins.Parameters.AddWithValue("e",admin.Email);ins.Parameters.AddWithValue("p",hash);ins.Parameters.AddWithValue("r",admin.Role);await ins.ExecuteNonQueryAsync();}}
}

record LoginRequest(string Email,string Password);
record CreateCompanyRequest(string Name,string AdminName,string AdminEmail,string AdminPassword);
record CompanyStatusRequest(string Status);
record UpdateCompanyRequest(string Name,string? LegalName,string? TaxId,string? Phone,string? Email,string? Address,string? LogoDataUrl);
record NotificationSettingsRequest(bool Enabled,bool Internal,bool EmailEnabled,string? Email,bool DueSoon,bool Overdue,int RepeatDays);
record MarkNotificationRequest(Guid VehicleId,Guid ServiceId,string Status,string? Channel,string? Recipient);
record CreateUserRequest(string FullName,string Email,string Password,string Role);
record TechnicianSelectVehicleRequest(Guid? VehicleId,string? QrToken);
record UserStatusRequest(string Status);
record UpdateUserRequest(string FullName,string Email,string Role);
record ResetUserPasswordRequest(string Password);
record ChangeOwnPasswordRequest(string CurrentPassword,string NewPassword);
record CreateVehicleRequest(string Plate,string? InternalNumber,string Brand,string Model,string? Variant,int CurrentMileage,Guid? PlanVersionId);
record UpdateVehicleRequest(string Plate,string? InternalNumber,string Brand,string Model,Guid PlanVersionId);
record ReactivateVehicleRequest(Guid PlanVersionId);
record MileageRequest(int Mileage,bool ExceptionConfirmed=false);
record HistoricalMileageRequest(int Mileage,DateTime ReadingDate);
record DemoUser(Guid Id,Guid CompanyId,string FullName,string Email,string PasswordHash,string Role);
record CreatePlanRequest(string Name,string Brand,string Model,string? Variant);
record UpdatePlanRequest(string Name,string Brand,string Model);
record CreateServiceRequest(string Name,string Category,string? Specification,int? IntervalKm,int? IntervalMonths,int? PrealertKm,int? PrealertDays);
record UpdateServiceRequest(string Name,string? Specification,int? IntervalKm,int? IntervalMonths,int? PrealertKm,int? PrealertDays);
record AssignPlanRequest(Guid PlanVersionId);
record BaselineRequest(Guid PlanServiceId,int? LastServiceMileage,DateTime? LastServiceDate);
record RegisterMaintenanceRequest(int Mileage,DateTime ServiceDate,List<Guid> ServiceIds,string? Notes,bool ExceptionConfirmed=false);
record MaintenanceStatusItem(Guid ServiceId,string Name,string Status,int? NextDueMileage,DateTime? NextDueDate,int? RemainingKm,int? RemainingDays);

record DashboardVehicle(Guid Id,string Plate,string? InternalNumber,string Brand,string Model,string? Variant,int CurrentMileage,List<MaintenanceStatusItem> Services);
record DashboardPriority(Guid VehicleId,string Plate,string? InternalNumber,string Brand,string Model,string? Variant,int CurrentMileage,Guid ServiceId,string ServiceName,string Status,int? NextDueMileage,int? RemainingKm,DateTime? NextDueDate,double? AverageKmPerDay,int? EstimatedDays,DateTime? EstimatedDate,int PredictionSampleDays);
record VehiclePrediction(double AverageKmPerDay,int SampleDays,double Variability,string Confidence);

