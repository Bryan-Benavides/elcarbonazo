using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/mesas")]
public sealed class MesasController(IMesasService mesasService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MesaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MesaDto>>> GetMesas(
        CancellationToken cancellationToken)
    {
        return Ok(await mesasService.GetAllAsync(cancellationToken));
    }

    [HttpPut("{id:int}/estado")]
    [Authorize(Roles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja + "," + UserRoles.Mesero + "," + UserRoles.Supervisor)]
    [ProducesResponseType<MesaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MesaDto>> UpdateEstado(
        int id,
        UpdateMesaEstadoRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mesasService.UpdateEstadoAsync(id, request, cancellationToken);

        return result.Outcome switch
        {
            UpdateMesaEstadoOutcome.Success => Ok(result.Mesa),
            UpdateMesaEstadoOutcome.InvalidState or
                UpdateMesaEstadoOutcome.InvalidTransition =>
                ProblemResult(
                    StatusCodes.Status400BadRequest,
                    "MESA_ESTADO_INVALIDO",
                    "Estado de mesa inválido",
                    result.Message ?? "La transición solicitada no es válida."),
            UpdateMesaEstadoOutcome.NotFound => ProblemResult(
                StatusCodes.Status404NotFound,
                "MESA_NO_ENCONTRADA",
                "Mesa no encontrada",
                "No existe una mesa con el identificador indicado."),
            UpdateMesaEstadoOutcome.PendingOrder => ProblemResult(
                StatusCodes.Status409Conflict,
                "MESA_CON_PEDIDO_PENDIENTE",
                "Mesa con pedido pendiente",
                result.Message ?? "No se puede liberar una mesa con un pedido pendiente."),
            UpdateMesaEstadoOutcome.GroupedConflict => ProblemResult(
                StatusCodes.Status409Conflict,
                "MESA_PERTENECE_A_GRUPO",
                "Mesa agrupada",
                result.Message ?? "La mesa pertenece a un grupo y no admite cambios individuales."),
            UpdateMesaEstadoOutcome.ConcurrencyConflict => ProblemResult(
                StatusCodes.Status409Conflict,
                "MESA_CONFLICTO_CONCURRENCIA",
                "La mesa cambió",
                "La versión enviada ya no es vigente. Recargue las mesas y confirme nuevamente."),
            _ => throw new InvalidOperationException($"Resultado no soportado: {result.Outcome}.")
        };
    }

    private ObjectResult ProblemResult(int status, string code, string title, string detail) =>
        new(ApiProblemDetails.Create(HttpContext, status, code, title, detail))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
