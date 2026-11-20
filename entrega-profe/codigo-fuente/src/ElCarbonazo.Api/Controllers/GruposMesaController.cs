using System.Security.Claims;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Route("api/grupos-mesa")]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja + "," + UserRoles.Mesero + "," + UserRoles.Supervisor)]
public sealed class GruposMesaController(IGruposMesaService gruposMesaService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<GrupoMesaDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        CreateGrupoMesaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return ProblemResult(
                StatusCodes.Status401Unauthorized,
                "AUTH_NO_AUTENTICADO",
                "Autenticación requerida",
                "El token no contiene un identificador de usuario válido.");
        }

        var result = await gruposMesaService.CreateAsync(request, userId, cancellationToken);
        if (result.Outcome == GrupoMesaOutcome.Success)
        {
            return StatusCode(StatusCodes.Status201Created, result.Grupo);
        }

        return result.Outcome switch
        {
            GrupoMesaOutcome.InvalidRequest => ProblemResult(
                StatusCodes.Status400BadRequest,
                "GRUPO_MESAS_NO_ELEGIBLES",
                "Solicitud de grupo inválida",
                result.Message ?? "El grupo requiere entre 2 y 15 mesas distintas."),
            GrupoMesaOutcome.NotEligible => ProblemResult(
                StatusCodes.Status409Conflict,
                "GRUPO_MESAS_NO_ELEGIBLES",
                "Mesas no elegibles",
                result.Message ?? "Las mesas ya no cumplen las condiciones para agruparse."),
            GrupoMesaOutcome.ConcurrencyConflict => ProblemResult(
                StatusCodes.Status409Conflict,
                "GRUPO_CONFLICTO_CONCURRENCIA",
                "Las mesas cambiaron",
                result.Message ?? "Recargue las mesas y confirme nuevamente."),
            _ => throw new InvalidOperationException($"Resultado de grupo no soportado: {result.Outcome}.")
        };
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return ProblemResult(
                StatusCodes.Status401Unauthorized,
                "AUTH_NO_AUTENTICADO",
                "Autenticación requerida",
                "El token no contiene un identificador de usuario válido.");
        }

        var result = await gruposMesaService.CloseAsync(id, userId, cancellationToken);
        return result.Outcome switch
        {
            GrupoMesaOutcome.Success => NoContent(),
            GrupoMesaOutcome.NotFound => ProblemResult(
                StatusCodes.Status404NotFound,
                "GRUPO_NO_ENCONTRADO",
                "Grupo no encontrado",
                "No existe un grupo con el identificador indicado."),
            GrupoMesaOutcome.PendingOrders => ProblemResult(
                StatusCodes.Status409Conflict,
                "GRUPO_CON_PEDIDOS_PENDIENTES",
                "Grupo con pedidos pendientes",
                result.Message ?? "No se puede cerrar el grupo mientras tenga pedidos pendientes."),
            _ => throw new InvalidOperationException($"Resultado de cierre no soportado: {result.Outcome}.")
        };
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out userId);

    private ObjectResult ProblemResult(int status, string code, string title, string detail) =>
        new(ApiProblemDetails.Create(HttpContext, status, code, title, detail))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
