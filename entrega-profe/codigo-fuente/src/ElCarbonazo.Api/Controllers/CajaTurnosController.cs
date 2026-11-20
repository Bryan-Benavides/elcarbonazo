using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.EncargadoCaja)]
[Route("api/caja")]
public sealed class CajaTurnosController(CajaTurnosService turnos) : ControllerBase
{
    [HttpGet("turno/actual")]
    public async Task<IActionResult> Actual(CancellationToken cancellationToken) =>
        Ok(new CajaTurnoActualDto(await turnos.ActualAsync(cancellationToken)));

    [HttpGet("turnos/{id:int}/resumen")]
    public async Task<IActionResult> Resumen(int id, CancellationToken cancellationToken)
    {
        var result = await turnos.PorIdAsync(id, cancellationToken);
        return result is not null ? Ok(result) : new ObjectResult(ApiProblemDetails.Create(HttpContext, 404,
            "RECURSO_NO_ENCONTRADO", "Recurso no encontrado", "El recurso solicitado no está disponible."))
        { StatusCode = 404, ContentTypes = { "application/problem+json" } };
    }
}
