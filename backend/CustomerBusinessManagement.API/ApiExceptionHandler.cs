using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CustomerBusinessManagement.API;

/// <summary>Beklenmeyen API hatalarını izlenebilir, standart Problem Details yanıtına dönüştürür.</summary>
public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(exception, "İstek işlenirken beklenmeyen hata oluştu. İz kimliği: {TraceId}", httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "İşlem tamamlanamadı",
            Detail = "Beklenmeyen bir hata oluştu. İz kimliği ile sistem yöneticisine başvurun.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110#name-500-internal-server-error",
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
