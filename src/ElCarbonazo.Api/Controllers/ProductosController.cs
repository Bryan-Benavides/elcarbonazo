using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoles.Jefa + "," + UserRoles.Supervisor + "," + UserRoles.Mesero + "," + UserRoles.EncargadoCaja)]
[Route("api/productos")]
public sealed class ProductosController(IProductosService productosService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProductoPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductoPageDto>> GetProductos(
        [FromQuery] bool disponibles = true,
        [FromQuery] string? cursor = null,
        [FromQuery] int limite = 25,
        CancellationToken cancellationToken = default)
    {
        if (!disponibles)
        {
            var problem = ApiProblemDetails.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Filtro inválido",
                "El catálogo de selección solo permite consultar productos disponibles.");
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["disponibles"] = ["Solo se admiten productos disponibles."]
            };
            return new ObjectResult(problem) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
        }

        if (limite is < 1 or > 100)
        {
            var problem = ApiProblemDetails.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Límite inválido",
                "El límite debe estar entre 1 y 100.");
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["limite"] = ["El valor debe estar entre 1 y 100."]
            };
            return BadRequest(problem);
        }

        var result = await productosService.GetPageAsync(disponibles, cursor, limite, cancellationToken);
        if (result.InvalidCursor)
        {
            var problem = ApiProblemDetails.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Cursor inválido",
                "El cursor no es válido para los filtros de esta consulta.");
            problem.Extensions["errors"] = new Dictionary<string, string[]>
            {
                ["cursor"] = ["Revise el cursor y los filtros de la consulta."]
            };
            return BadRequest(problem);
        }

        return Ok(result.Page);
    }
}
