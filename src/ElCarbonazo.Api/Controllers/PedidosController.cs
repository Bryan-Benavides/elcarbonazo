using System.Security.Claims;
using System.Text.Json;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.Mesero + "," + UserRoles.EncargadoCaja)]
[Route("api/pedidos")]
public sealed class PedidosController(ICuentasService cuentas, EnviosService envios) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;

    [HttpPost("envios")]
    public async Task<IActionResult> Enviar(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
        {
            return Error(401, "AUTH_NO_AUTENTICADO");
        }

        if (Request.ContentLength > MaxBodyBytes)
        {
            return Error(413, "REQUEST_TOO_LARGE");
        }

        var buffer = new byte[MaxBodyBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count > MaxBodyBytes) return Error(413, "REQUEST_TOO_LARGE");

        EnvioRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<EnvioRequest>(buffer.AsSpan(0, count),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Error(400, "VALIDATION_ERROR");
        }

        if (request is null) return Error(400, "VALIDATION_ERROR");
        var result = await envios.EnviarAsync(request, userId, cancellationToken);
        if (result.Status == 201)
        {
            Response.Headers.Location = result.Location;
            return new ContentResult { Content = result.Json, ContentType = "application/json", StatusCode = 201 };
        }
        return Error(result.Status, result.Code!, result.Data);
    }

    private ObjectResult Error(int status, string code, object? data = null)
    {
        var problem = ApiProblemDetails.Create(HttpContext, status, code,
            status == 400 ? "Solicitud inválida" : "No se pudo completar el envío",
            "Revise la solicitud y el estado actual antes de continuar.");
        if (status == 400)
        {
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["solicitud"] = ["La solicitud no cumple los requisitos de envío."]
            };
        }
        if (data is not null) problem.Extensions[code == "STOCK_INSUFICIENTE" ? "ingredientes" : "productos"] = data;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    [HttpGet("activo")]
    public async Task<ActionResult<CuentaActivaDto>> GetActivo(
        [FromQuery] int? mesaId, [FromQuery] int? grupoId, CancellationToken cancellationToken)
    {
        if ((mesaId is null) == (grupoId is null) || mesaId is <= 0 || grupoId is <= 0)
        {
            var problem = ApiProblemDetails.Create(HttpContext, StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR", "Destino inválido", "Indique exactamente una mesa o grupo con identificador positivo.");
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["destino"] = ["Indique exactamente una mesa o grupo con identificador positivo."]
            };
            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        }

        var result = await cuentas.GetActivaAsync(mesaId, grupoId, cancellationToken);
        if (result is null)
        {
            return new ObjectResult(ApiProblemDetails.Create(HttpContext, StatusCodes.Status404NotFound,
                "HTTP_NO_ENCONTRADO", "Destino no encontrado", "El destino no existe o no está disponible."))
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentTypes = { "application/problem+json" }
            };
        }

        return Ok(result);
    }
}
