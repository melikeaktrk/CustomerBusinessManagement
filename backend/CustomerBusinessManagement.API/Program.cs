using System.Security.Claims;
using System.Text;
using CustomerBusinessManagement.API;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

// İstek gövdelerindeki DTO'ları, merkezi doğrulama filtresiyle denetle.
builder.Services.AddControllers(o => o.Filters.AddService<FluentValidationActionFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<FluentValidationActionFilter>();
builder.Services.AddDataProtection().AddKeyManagementOptions(options =>
    options.XmlRepository = new InMemoryXmlRepository()
);
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<CustomerRequest>, CustomerRequestValidator>();
builder.Services.AddScoped<IValidator<EmployeeRequest>, EmployeeRequestValidator>();
builder.Services.AddScoped<IValidator<AccountingRequest>, AccountingRequestValidator>();
builder.Services.AddScoped<IValidator<CashRequest>, CashRequestValidator>();
builder.Services.AddScoped<IValidator<ZReportRequest>, ZReportRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Admin JWT token",
        }
    );
    o.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                },
                Array.Empty<string>()
            },
        }
    );
});
builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Standart ASP.NET Core Identity rol sistemi için gerekli tablolari ve role yönetimini kur.
builder
    .Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
var key =
    builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key must be configured through environment or secret storage."
    );
if (Encoding.UTF8.GetByteCount(key) < 32)
    throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes and must be supplied through User Secrets or environment variables.");

// API uç noktalarında imzalı JWT belirteçlerini doğrula.
builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Bearer";
        options.DefaultChallengeScheme = "Bearer";
        options.DefaultForbidScheme = "Bearer";
    })
    .AddJwtBearer(
        "Bearer",
        o =>
            o.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "CustomerBusinessManagement",
                ValidateAudience = true,
                ValidAudience =
                    builder.Configuration["Jwt:Audience"] ?? "CustomerBusinessManagement",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true,
                NameClaimType = ClaimTypes.NameIdentifier,
            }
    );
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(
        AuthorizationPolicies.SuperAdmin,
        AuthorizationPolicies.CreatePolicy(AuthorizationRoles.SuperAdmin)
    );
    o.AddPolicy(
        AuthorizationPolicies.Admin,
        AuthorizationPolicies.AdminPolicy
    );
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ManagementService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IZReportOcrService, ZReportOcrService>();
builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p =>
        p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
    )
);
var app = builder.Build();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer Business Management API V1");
    c.RoutePrefix = string.Empty;
});
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    foreach (var roleName in new[] { AuthorizationRoles.Admin, AuthorizationRoles.SuperAdmin })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    $"Role creation failed for {roleName}: {string.Join(";", roleResult.Errors.Select(e => e.Description))}"
                );
        }
    }

    var adminEmail = builder.Configuration["Admin:Email"];
    var adminPassword = builder.Configuration["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin is null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FirstName = "System",
                LastName = "Admin",
                IsActive = true,
            };
            var createResult = await userManager.CreateAsync(adminUser, adminPassword);
            if (!createResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(";", createResult.Errors.Select(e => e.Description))
                );
            var roleResult = await userManager.AddToRoleAsync(adminUser, AuthorizationRoles.Admin);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(";", roleResult.Errors.Select(e => e.Description))
                );
        }
        else if (!await userManager.IsInRoleAsync(existingAdmin, AuthorizationRoles.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(existingAdmin, AuthorizationRoles.Admin);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(";", roleResult.Errors.Select(e => e.Description))
                );
        }
    }

    var superAdminEmail = builder.Configuration["SuperAdmin:Email"];
    var superAdminPassword = builder.Configuration["SuperAdmin:Password"];
    if (!string.IsNullOrWhiteSpace(superAdminEmail) && !string.IsNullOrWhiteSpace(superAdminPassword))
    {
        var existingSuperAdmin = await userManager.FindByEmailAsync(superAdminEmail);
        if (existingSuperAdmin is null)
        {
            var superAdminUser = new ApplicationUser
            {
                UserName = superAdminEmail,
                Email = superAdminEmail,
                EmailConfirmed = true,
                FirstName = "System",
                LastName = "SuperAdmin",
                IsActive = true,
            };
            var createResult = await userManager.CreateAsync(superAdminUser, superAdminPassword);
            if (!createResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(";", createResult.Errors.Select(e => e.Description))
                );
            var roleResult = await userManager.AddToRoleAsync(superAdminUser, AuthorizationRoles.SuperAdmin);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(";", roleResult.Errors.Select(e => e.Description))
                );
        }
        else if (!await userManager.IsInRoleAsync(existingSuperAdmin, AuthorizationRoles.SuperAdmin))
        {
            var roleResult = await userManager.AddToRoleAsync(existingSuperAdmin, AuthorizationRoles.SuperAdmin);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    string.Join(";", roleResult.Errors.Select(e => e.Description))
                );
        }
    }
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsProduction())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
        app.Logger.LogInformation("Pending Entity Framework Core migrations were applied successfully.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "An error occurred while applying Entity Framework Core migrations. The application will continue running."
        );
    }
}

app.Run();

public partial class Program { }

sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly object sync = new();
    private readonly List<XElement> elements = new();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (sync)
        {
            return elements.Select(element => new XElement(element)).ToArray();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (sync)
        {
            elements.Add(new XElement(element));
        }
    }
}
