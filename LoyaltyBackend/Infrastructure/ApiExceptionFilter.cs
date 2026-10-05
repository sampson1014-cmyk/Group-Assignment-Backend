using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LoyaltyBackend.Infrastructure;

public sealed class ApiExceptionFilter(
    ILogger<ApiExceptionFilter> logger,
    IWebHostEnvironment environment) : IExceptionFilter
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

        if (environment.IsDevelopment())
        {
            context.HttpContext.Items["LoyaltyDiagnostic"] = GetSafeDiagnostic(context.Exception);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Result = new ViewResult { ViewName = "ServiceUnavailable" };
        context.ExceptionHandled = true;
    }

    private static string GetSafeDiagnostic(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: not null } requestException =>
            $"The loyalty API returned HTTP {(int)requestException.StatusCode.Value} ({requestException.StatusCode.Value}).",
        HttpRequestException => "The application could not connect to the loyalty API.",
        TaskCanceledException => "The loyalty API request timed out.",
        JsonException => "The loyalty API returned data in an unexpected format.",
        InvalidOperationException operationException
            when operationException.Message.StartsWith("LoyaltyApi:", StringComparison.Ordinal) =>
                operationException.Message,
        InvalidOperationException operationException
            when operationException.Message.StartsWith("The Loyalty API", StringComparison.Ordinal) =>
                operationException.Message,
        InvalidOperationException operationException => RedactDiagnostic(operationException.Message),
        _ => "The loyalty request failed unexpectedly."
    };

    private static string RedactDiagnostic(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "The loyalty request could not be completed because the application state was invalid.";
        }

        var sanitized = Regex.Replace(message, @"https?://\S+", "[URL redacted]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)\b(username|password|token|authorization)\s*[=:]\s*[^,;\s]+",
            "$1=[redacted]");
        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }
}
