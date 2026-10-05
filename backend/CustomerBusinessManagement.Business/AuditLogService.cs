using System.Security.Claims;
using CustomerBusinessManagement.DataAccess;
using CustomerBusinessManagement.Entities;
using Microsoft.AspNetCore.Http;

namespace CustomerBusinessManagement.Business;

public interface IAuditLogService
{
    Task RecordAsync(
        string action,
        string entityName,
        string entityId,
        string description,
        CancellationToken cancellationToken = default
    );
}

public sealed class AuditLogService(ApplicationDbContext db, IHttpContextAccessor http)
    : IAuditLogService
{
    public async Task RecordAsync(
        string action,
        string entityName,
        string entityId,
        string description,
        CancellationToken cancellationToken = default
    )
    {
        db.AuditLogs.Add(
            new AuditLog
            {
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Description = description,
                UserId =
                    http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? http.HttpContext?.User.FindFirstValue("sub"),
            }
        );
        await db.SaveChangesAsync(cancellationToken);
    }
}
