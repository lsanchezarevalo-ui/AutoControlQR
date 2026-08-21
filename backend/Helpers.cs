using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

internal static class Helpers
{
    public static string ToTitleCase(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        var ti=System.Globalization.CultureInfo.GetCultureInfo("es-CO").TextInfo;
        return ti.ToTitleCase(value.Trim().ToLowerInvariant());
    }

    public static string CreateJwt(DemoUser user,string jwtKey,Guid? workVehicleId=null)
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
    public static bool CanAccessVehicle(ClaimsPrincipal p,Guid vehicleId)
    {
        if(p.FindFirstValue("role")=="COMPANY_ADMIN")return true;
        if(p.FindFirstValue("role")!="TECHNICIAN")return false;
        var x=p.FindFirstValue("work_vehicle_id");
        return Guid.TryParse(x,out var id)&&id==vehicleId;
    }

    public static Guid CompanyId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("company_id")!);
    public static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("user_id")!);

    // Catálogo de servicios: reutiliza un servicio existente por nombre (insensible a mayúsculas) o lo crea si no existe,
    // para que el mismo servicio conserve una identidad estable sin importar en qué plan o versión se use.
    public static async Task<Guid> ResolveOrCreateCompanyService(NpgsqlConnection con,NpgsqlTransaction? tx,Guid companyId,Guid userId,string name,string category,string? specification,int? intervalKm,int? intervalMonths,int? prealertKm,int? prealertDays)
    {
        var trimmedName=name.Trim();
        await using(var find=new NpgsqlCommand("SELECT id FROM company_services WHERE company_id=@c AND lower(name)=lower(@n) AND active=true LIMIT 1",con,tx))
        {find.Parameters.AddWithValue("c",companyId);find.Parameters.AddWithValue("n",trimmedName);if(await find.ExecuteScalarAsync() is Guid existing)return existing;}
        var id=Guid.NewGuid();
        try
        {
            await using var ins=new NpgsqlCommand(@"INSERT INTO company_services(id,company_id,name,category,specification,default_interval_km,default_interval_months,default_prealert_km,default_prealert_days,created_by_user_id) VALUES(@id,@c,@n,@cat,@sp,@ik,@im,@pk,@pd,@u)",con,tx);
            ins.Parameters.AddWithValue("id",id);ins.Parameters.AddWithValue("c",companyId);ins.Parameters.AddWithValue("n",trimmedName);ins.Parameters.AddWithValue("cat",string.IsNullOrWhiteSpace(category)?"General":category.Trim());
            ins.Parameters.AddWithValue("sp",(object?)specification??DBNull.Value);ins.Parameters.AddWithValue("ik",(object?)intervalKm??DBNull.Value);ins.Parameters.AddWithValue("im",(object?)intervalMonths??DBNull.Value);
            ins.Parameters.AddWithValue("pk",(object?)prealertKm??DBNull.Value);ins.Parameters.AddWithValue("pd",(object?)prealertDays??DBNull.Value);ins.Parameters.AddWithValue("u",userId);
            await ins.ExecuteNonQueryAsync();
            return id;
        }
        catch(PostgresException ex) when(ex.SqlState=="23505")
        {
            await using var find2=new NpgsqlCommand("SELECT id FROM company_services WHERE company_id=@c AND lower(name)=lower(@n) AND active=true LIMIT 1",con,tx);
            find2.Parameters.AddWithValue("c",companyId);find2.Parameters.AddWithValue("n",trimmedName);
            return (Guid)(await find2.ExecuteScalarAsync())!;
        }
    }
    public static object ServiceObject(NpgsqlDataReader r) => new {id=r.GetGuid(0),name=r.GetString(1),category=r.GetString(2),specification=r.IsDBNull(3)?null:r.GetString(3),intervalKm=r.IsDBNull(4)?(int?)null:r.GetInt32(4),intervalMonths=r.IsDBNull(5)?(int?)null:r.GetInt32(5),prealertKm=r.IsDBNull(6)?(int?)null:r.GetInt32(6),prealertDays=r.IsDBNull(7)?(int?)null:r.GetInt32(7)};
    public static MaintenanceStatusItem CalcStatus(Guid id,string name,int currentMileage,int? intervalKm,int? intervalMonths,int prealertKm,int prealertDays,int? lastKm,DateTime? lastDate)
    {
        if(lastKm is null && lastDate is null) return new(id,name,"NO_BASELINE",null,null,null,null,lastKm,lastDate);
        int? nextKm = intervalKm.HasValue && lastKm.HasValue ? lastKm.Value + intervalKm.Value : null;
        DateTime? nextDate = intervalMonths.HasValue && lastDate.HasValue ? lastDate.Value.Date.AddMonths(intervalMonths.Value) : null;
        int? remainingKm = nextKm.HasValue ? nextKm.Value-currentMileage : null;
        int? remainingDays = nextDate.HasValue ? (nextDate.Value.Date-DateTime.UtcNow.Date).Days : null;
        var overdue=(remainingKm.HasValue && remainingKm.Value<=0)||(remainingDays.HasValue && remainingDays.Value<=0);
        var soon=!overdue && ((remainingKm.HasValue && remainingKm.Value<=prealertKm)||(remainingDays.HasValue && remainingDays.Value<=prealertDays));
        return new(id,name,overdue?"OVERDUE":soon?"DUE_SOON":"UP_TO_DATE",nextKm,nextDate,remainingKm,remainingDays,lastKm,lastDate);
    }
}
