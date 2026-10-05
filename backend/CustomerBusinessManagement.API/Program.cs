using System.Text;
using CustomerBusinessManagement.API;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// İstek gövdelerindeki DTO'ları, merkezi doğrulama filtresiyle denetle.
builder.Services.AddControllers(o => o.Filters.AddService<FluentValidationActionFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<FluentValidationActionFilter>();
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

// Kimlik doğrulamayı uygulama kullanıcısı ve EF Core deposuna bağla.
builder
    .Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();
var key =
    builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key must be configured through environment or secret storage."
    );
if (Encoding.UTF8.GetByteCount(key) < 32)
    throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes and must be supplied through User Secrets or environment variables.");

// API uç noktalarında imzalı JWT belirteçlerini doğrula.
builder
    .Services.AddAuthentication("Bearer")
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
            }
    );
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ManagementService>();
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    using var scope = app.Services.CreateScope();
    var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var email = builder.Configuration["Admin:Email"];
    var password = builder.Configuration["Admin:Password"];
    if (
        !string.IsNullOrWhiteSpace(email)
        && !string.IsNullOrWhiteSpace(password)
        && await manager.FindByEmailAsync(email) == null
    )
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Admin",
        };
        var result = await manager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(";", result.Errors.Select(e => e.Description))
            );
    }
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
