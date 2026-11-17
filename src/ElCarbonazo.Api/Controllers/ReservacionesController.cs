using System.Security.Claims;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reservaciones")]
public sealed class ReservacionesController(ReservacionesService service) : ControllerBase
{
    private const string SalonRoles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja + "," +
                                      UserRoles.Mesero + "," + UserRoles.Supervisor;

    [HttpGet("disponibilidad")]
    public async Task<IActionResult> Disponibilidad(
        [FromQuery] DateTimeOffset? desdeUtc, [FromQuery] DateTimeOffset? hastaUtc,
        [FromQuery] int personas = 1, CancellationToken ct = default)
    {
        if (desdeUtc is null || hastaUtc is null) return Error(400, "VALIDATION_ERROR", "Falta el intervalo solicitado.");
        var result = await service.DisponibilidadAsync(desdeUtc.Value, hastaUtc.Value, personas, ct);
        return result.Status == 200 ? Ok(result.Disponibles) : Error(result.Status, result.Code, result.Message);
    }

    [HttpGet]
    [Authorize(Roles = SalonRoles + "," + UserRoles.PersonalCocina)]
    public async Task<IActionResult> Listar(
        [FromQuery] DateTimeOffset? desdeUtc, [FromQuery] DateTimeOffset? hastaUtc, CancellationToken ct)
    {
        if (desdeUtc is null || hastaUtc is null) return Error(400, "VALIDATION_ERROR", "Falta el intervalo de consulta.");
        var result = await service.ListarAsync(desdeUtc.Value, hastaUtc.Value, ct);
        return result.Status == 200 ? Ok(result.Items) : Error(result.Status, result.Code, result.Message);
    }

    [HttpPost]
    [Authorize(Roles = SalonRoles)]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Crear(CrearReservacionRequest? request, CancellationToken ct)
    {
        if (!TryActor(out var actor)) return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await service.CrearAsync(request, actor, ct);
        if (result.Status == 201) return StatusCode(201, result.Reservacion);
        return Error(result.Status, result.Code, result.Message);
    }

    [HttpPost("{id:int}/cancelar")]
    [Authorize(Roles = SalonRoles)]
    public async Task<IActionResult> Cancelar(int id, CancellationToken ct)
    {
        if (!TryActor(out var actor)) return Error(401, "AUTH_NO_AUTENTICADO");
        var result = await service.CancelarAsync(id, actor, ct);
        return result.Status == 200 ? Ok(result.Reservacion) : Error(result.Status, result.Code, result.Message);
    }

    private bool TryActor(out int actor) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out actor);

    private ObjectResult Error(int status, string code, string? message = null)
    {
        var title = code switch
        {
            "RESERVACION_CONFLICTO" => "Horario no disponible",
            "MESA_NO_DISPONIBLE" => "Mesa no disponible",
            "MESA_NO_ENCONTRADA" => "Mesa no encontrada",
            _ => "No se pudo procesar la reservación"
        };
        return new(ApiProblemDetails.Create(HttpContext, status, code, title,
            message ?? "Revise los datos y el estado de la mesa."))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
