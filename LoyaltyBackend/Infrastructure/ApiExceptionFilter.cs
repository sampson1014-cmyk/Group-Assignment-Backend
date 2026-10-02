using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LoyaltyBackend.Infrastructure;

public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not (HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException))
        {
            return;
        }

        // Avoid logging exception messages because the provider's JWT endpoint
        // uses query-string credentials and some responses contain member PII.
        logger.LogError("Loyalty service request failed. Exception type: {ExceptionType}", context.Exception.GetType().Name);
        context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Result = new ViewResult { ViewName = "ServiceUnavailable" };
        context.ExceptionHandled = true;
    }
}
