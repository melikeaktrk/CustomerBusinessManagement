using System.Security.Claims;
using CustomerBusinessManagement.Business;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.DTO;
using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerBusinessManagement.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
[ApiController]
[Route("api/[controller]")]
public class UsersController(
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    IAuditLogService audit
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await db.Users.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var result = new List<UserResponse>();
        foreach (var user in items)
        {
            var roles = await users.GetRolesAsync(user);
            result.Add(new UserResponse(
                user.Id,
                user.Email ?? "",
                user.UserName ?? "",
                user.FirstName,
                user.LastName,
                roles.FirstOrDefault() ?? "",
                user.IsActive,
                user.CreatedAt
            ));
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (!IsSupportedRole(request.Role))
            return BadRequest(new { message = "Geçersiz rol seçildi." });
        if (await users.FindByEmailAsync(request.Email) is not null)
            return Conflict(new { message = "Bu e-posta zaten kullanılıyor." });
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = true,
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(x => x.Code, x => new[] { x.Description })
            ));
        var roleResult = await users.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(
                roleResult.Errors.ToDictionary(x => x.Code, x => new[] { x.Description })
            ));
        await audit.RecordAsync("Create", nameof(ApplicationUser), user.Id, $"{request.Role} kullanıcı oluşturuldu.");
        return CreatedAtAction(nameof(Get), new { id = user.Id }, await ToResponseAsync(user));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id) =>
        await users.FindByIdAsync(id) is { } user
            ? Ok(await ToResponseAsync(user))
            : NotFound();

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (!IsSupportedRole(request.Role))
            return BadRequest(new { message = "Geçersiz rol seçildi." });
        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            var removeResult = await users.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Kullanıcı rolü güncellenemedi." });
        }
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.IsActive = request.IsActive;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Kullanıcı güncellenemedi." });
        var addResult = await users.AddToRoleAsync(user, request.Role);
        if (!addResult.Succeeded)
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Kullanıcı rolü güncellenemedi." });
        await audit.RecordAsync("Update", nameof(ApplicationUser), user.Id, "Kullanıcı güncellendi.");
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (id == User.FindFirst(ClaimTypes.NameIdentifier)?.Value) return Conflict(new { message = "Kendi hesabınız silinemez." });
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        user.IsActive = false;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Kullanıcı pasife alınamadı." });
        await audit.RecordAsync("Delete", nameof(ApplicationUser), user.Id, "Kullanıcı pasife alındı.");
        return NoContent();
    }

    private static bool IsSupportedRole(string role) =>
        role == AuthorizationRoles.SuperAdmin || role == AuthorizationRoles.Admin;

    private async Task<UserResponse> ToResponseAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return new UserResponse(
            user.Id,
            user.Email ?? "",
            user.UserName ?? "",
            user.FirstName,
            user.LastName,
            roles.FirstOrDefault() ?? "",
            user.IsActive,
            user.CreatedAt
        );
    }
}
