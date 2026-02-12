using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AuthHub.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        _logger.LogError(ex, "Unhandled exception");

        
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        
        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : context.Request.Headers["X-Correlation-Id"].ToString();

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Server error",
            Detail = ex.Message, 
            Instance = context.Request.Path
        };

        
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(correlationId))
            problem.Extensions["correlationId"] = correlationId;

        var json = JsonSerializer.Serialize(problem, _jsonOptions);
        await context.Response.WriteAsync(json);
    }
}