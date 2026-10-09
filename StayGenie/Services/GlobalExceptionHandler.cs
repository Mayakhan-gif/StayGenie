using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace StayGenie.Services;

// Koi bhi unhandled error: andar ki detail sirf log mein, user ko saaf aur safe jawab
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Unhandled exception on {Method} {Path}",
            ctx.Request.Method, ctx.Request.Path);

        var (status, title) = ex switch
        {
            DbUpdateConcurrencyException => (409, "The data was changed by someone else. Please retry."),
            DbUpdateException => (409, "The request conflicts with existing data."),
            HttpRequestException => (502, "An external service is not responding."),
            BadHttpRequestException => (400, "The request is not valid."),
            _ => (500, "Something went wrong on our side.")
        };

        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["traceId"] = ctx.TraceIdentifier;

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}