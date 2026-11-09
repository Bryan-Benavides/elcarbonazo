using System.Security.Claims;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.PersonalCocina)]
[Route("api/cocina")]
public sealed class CocinaController(CocinaService cocina) : ControllerBase
{
    [HttpGet("cola")]
    public async Task<IActionResult> Cola([FromQuery] string? cursor, [FromQuery] int limite = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await cocina.ColaAsync(cursor, limite, cancellationToken);
        return result.Status == 200 ? Ok(result.Pagina) : Error(result);
    }

    [HttpPut("comandas/{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, CambiarComandaEstadoRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
            return Error(new CocinaResult(401, "AUTH_NO_AUTENTICADO"));
        var result = await cocina.CambiarEstadoAsync(id, request, userId, cancellationToken);
        return result.Status == 200 ? Ok(result.Comanda) : Error(result);
    }

    private ObjectResult Error(CocinaResult result)
    {
        var problem = ApiProblemDetails.Create(HttpContext, result.Status, result.Code!,
            result.Status == 400 ? "Solicitud inválida" : "No se pudo procesar la comanda",
            "Revise la solicitud y el estado actual antes de continuar.");
        if (result.Status == 400)
        {
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["solicitud"] = ["La solicitud no cumple los requisitos de cocina."]
            };
        }
        return new ObjectResult(problem) { StatusCode = result.Status, ContentTypes = { "application/problem+json" } };
    }
}
