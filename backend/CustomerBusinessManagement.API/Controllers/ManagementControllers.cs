using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CustomerBusinessManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(UserManager<ApplicationUser> users, IConfiguration config)
    : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user == null || !user.IsActive || !await users.CheckPasswordAsync(user, req.Password))
            return Unauthorized(new { message = "E-posta veya parola hatalı." });
        var expiry = DateTime.UtcNow.AddHours(8);
        var key =
            config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName!),
        };
        var token = new JwtSecurityToken(
            config["Jwt:Issuer"] ?? "CustomerBusinessManagement",
            config["Jwt:Audience"] ?? "CustomerBusinessManagement",
            claims,
            expires: expiry,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return Ok(
            new LoginResponse(
                new JwtSecurityTokenHandler().WriteToken(token),
                expiry,
                user.Id,
                user.Email!,
                user.UserName!
            )
        );
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var u = await users.GetUserAsync(User);
        return u == null
            ? Unauthorized()
            : Ok(
                new
                {
                    u.Id,
                    u.Email,
                    u.UserName,
                    u.FirstName,
                    u.LastName,
                }
            );
    }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CustomersController(ApplicationDbContext db, IAuditLogService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
    {
        var q = db.Customers.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(x =>
                x.FirstName.Contains(search)
                || x.LastName.Contains(search)
                || x.CompanyName!.Contains(search)
                || x.CustomerCode.Contains(search)
            );
        return Ok(await q.OrderByDescending(x => x.CreatedAt).ToListAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) =>
        await db.Customers.FindAsync(id) is { } x ? Ok(x) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(CustomerRequest r)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var x = new Customer
        {
            CustomerCode = r.CustomerCode,
            FirstName = r.FirstName,
            LastName = r.LastName,
            CompanyName = r.CompanyName,
            Phone = r.Phone,
            Email = r.Email,
            Address = r.Address,
            TaxNumber = r.TaxNumber,
            IsActive = r.IsActive,
        };
        db.Add(x);
        await db.SaveChangesAsync();
        await audit.RecordAsync(
            "Create",
            x.GetType().Name,
            x.Id.ToString(),
            $"{x.GetType().Name} created"
        );
        return CreatedAtAction(nameof(Get), new { id = x.Id }, x);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CustomerRequest r)
    {
        var x = await db.Customers.FindAsync(id);
        if (x == null)
            return NotFound();
        x.CustomerCode = r.CustomerCode;
        x.FirstName = r.FirstName;
        x.LastName = r.LastName;
        x.CompanyName = r.CompanyName;
        x.Phone = r.Phone;
        x.Email = r.Email;
        x.Address = r.Address;
        x.TaxNumber = r.TaxNumber;
        x.IsActive = r.IsActive;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Update", nameof(Customer), id.ToString(), $"Müşteri güncellendi: {x.FirstName} {x.LastName}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.Customers.FindAsync(id);
        if (x == null)
            return NotFound();
        x.IsActive = false;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Delete", nameof(Customer), id.ToString(), $"Müşteri pasife alındı: {x.FirstName} {x.LastName}");
        return NoContent();
    }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EmployeesController(ApplicationDbContext db, IAuditLogService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.Employees.AsNoTracking().Where(x => x.IsActive).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) =>
        await db.Employees.FindAsync(id) is { } x ? Ok(x) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(EmployeeRequest r)
    {
        if (r.Salary < 0)
            return BadRequest();
        var x = new Employee
        {
            FirstName = r.FirstName,
            LastName = r.LastName,
            Phone = r.Phone,
            Email = r.Email,
            Position = r.Position,
            HireDate = r.HireDate,
            Salary = r.Salary,
            IsActive = r.IsActive,
        };
        db.Add(x);
        await db.SaveChangesAsync();
        await audit.RecordAsync(
            "Create",
            x.GetType().Name,
            x.Id.ToString(),
            $"{x.GetType().Name} created"
        );
        return CreatedAtAction(nameof(Get), new { id = x.Id }, x);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, EmployeeRequest r)
    {
        var x = await db.Employees.FindAsync(id);
        if (x == null)
            return NotFound();
        x.FirstName = r.FirstName;
        x.LastName = r.LastName;
        x.Phone = r.Phone;
        x.Email = r.Email;
        x.Position = r.Position;
        x.HireDate = r.HireDate;
        x.Salary = r.Salary;
        x.IsActive = r.IsActive;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Update", nameof(Employee), id.ToString(), $"Personel güncellendi: {x.FirstName} {x.LastName}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.Employees.FindAsync(id);
        if (x == null)
            return NotFound();
        x.IsActive = false;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Delete", nameof(Employee), id.ToString(), $"Personel pasife alındı: {x.FirstName} {x.LastName}");
        return NoContent();
    }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AccountingTransactionsController(
    ApplicationDbContext db,
    CustomerBusinessManagement.Business.ManagementService svc,
    IAuditLogService audit
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(
            await db
                .AccountingTransactions.AsNoTracking()
                .Where(x => !x.IsCancelled)
                .OrderByDescending(x => x.TransactionDate)
                .ToListAsync()
        );

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(await svc.AccountingSummary());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) =>
        await db.AccountingTransactions.FindAsync(id) is { } x ? Ok(x) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(AccountingRequest r)
    {
        if (r.Amount <= 0 || !Enum.TryParse<AccountingType>(r.TransactionType, true, out var type))
            return BadRequest();
        var x = new AccountingTransaction
        {
            TransactionType = type,
            Amount = r.Amount,
            Description = r.Description,
            TransactionDate = r.TransactionDate,
            Category = r.Category,
            ReferenceNumber = r.ReferenceNumber,
        };
        db.Add(x);
        await db.SaveChangesAsync();
        await audit.RecordAsync(
            "Create",
            x.GetType().Name,
            x.Id.ToString(),
            $"{x.GetType().Name} created"
        );
        return CreatedAtAction(nameof(Get), new { id = x.Id }, x);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, AccountingRequest r)
    {
        var x = await db.AccountingTransactions.FindAsync(id);
        if (x == null)
            return NotFound();
        if (r.Amount <= 0 || !Enum.TryParse<AccountingType>(r.TransactionType, true, out var t))
            return BadRequest();
        x.TransactionType = t;
        x.Amount = r.Amount;
        x.Description = r.Description;
        x.TransactionDate = r.TransactionDate;
        x.Category = r.Category;
        x.ReferenceNumber = r.ReferenceNumber;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Update", nameof(AccountingTransaction), id.ToString(), $"Muhasebe kaydı güncellendi: {x.Description}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.AccountingTransactions.FindAsync(id);
        if (x == null)
            return NotFound();
        x.IsCancelled = true;
        await db.SaveChangesAsync();
        await audit.RecordAsync("Delete", nameof(AccountingTransaction), id.ToString(), $"Muhasebe kaydı iptal edildi: {x.Description}");
        return NoContent();
    }
}
