using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/mesas")]
public sealed class MesasController(AppDbContext context) : ControllerBase
{
    private static readonly HashSet<string> EstadosValidos = new(StringComparer.Ordinal)
    {
        "Libre",
        "Ocupada",
        "Reservada",
        "Agrupada"
    };

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MesaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MesaDto>>> GetMesas(
        CancellationToken cancellationToken)
    {
        var mesas = await context.Mesas
            .AsNoTracking()
            .OrderBy(mesa => mesa.Numero)
            .Select(mesa => new MesaDto(
                mesa.IdMesa,
                mesa.IdGrupo,
                mesa.Numero,
                mesa.Capacidad,
                mesa.Estado))
            .ToListAsync(cancellationToken);

        return Ok(mesas);
    }

    [HttpPut("{id:int}/estado")]
    [ProducesResponseType<MesaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MesaDto>> UpdateEstado(
        int id,
        UpdateMesaEstadoRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Estado is null || !EstadosValidos.Contains(request.Estado))
        {
            return BadRequest(new
            {
                message = "Estado inválido. Use Libre, Ocupada, Reservada o Agrupada."
            });
        }

        var mesa = await context.Mesas.FindAsync([id], cancellationToken);
        if (mesa is null)
        {
            return NotFound();
        }

        if (request.Estado == "Agrupada" && mesa.IdGrupo is null)
        {
            return BadRequest(new
            {
                message = "Una mesa agrupada debe tener un grupo asignado."
            });
        }

        if (request.Estado == "Libre")
        {
            var existeTablaPedido = await context.Database
                .SqlQuery<bool>($"""
                    SELECT to_regclass('public.pedido') IS NOT NULL AS "Value"
                    """)
                .SingleAsync(cancellationToken);

            if (existeTablaPedido)
            {
                var tienePedidoPendiente = await context.Database
                    .SqlQuery<bool>($"""
                        SELECT EXISTS (
                            SELECT 1
                            FROM pedido
                            WHERE id_mesa = {id}
                              AND estado IN ('Abierto', 'En Preparación', 'Listo')
                        ) AS "Value"
                        """)
                    .SingleAsync(cancellationToken);

                if (tienePedidoPendiente)
                {
                    return Conflict(new
                    {
                        message = "No se puede liberar una mesa con una cuenta pendiente."
                    });
                }
            }
        }

        if (mesa.Estado == "Agrupada" && request.Estado != "Agrupada")
        {
            mesa.IdGrupo = null;
        }

        mesa.Estado = request.Estado;
        await context.SaveChangesAsync(cancellationToken);

        return Ok(new MesaDto(
            mesa.IdMesa,
            mesa.IdGrupo,
            mesa.Numero,
            mesa.Capacidad,
            mesa.Estado));
    }
}
