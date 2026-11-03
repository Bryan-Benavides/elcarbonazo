using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ElCarbonazo.Api.Http;

public static class ApiProblemDetails
{
    public static ProblemDetails Create(
        HttpContext context,
        int status,
        string code,
        string title,
        string detail) => new()
        {
            Type = $"https://elcarbonazo.local/problems/{code.ToLowerInvariant()}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
        {
            ["code"] = code,
            ["traceId"] = context.TraceIdentifier
        }
        };

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        string detail,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            Create(context, status, code, title, detail),
            JsonSerializerOptions.Web,
            cancellationToken);
    }
}
