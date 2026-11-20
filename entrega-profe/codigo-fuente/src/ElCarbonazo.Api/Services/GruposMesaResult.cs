using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public enum GrupoMesaOutcome
{
    Success,
    InvalidRequest,
    NotFound,
    NotEligible,
    ConcurrencyConflict,
    PendingOrders
}

public sealed record GruposMesaResult(
    GrupoMesaOutcome Outcome,
    GrupoMesaDto? Grupo = null,
    string? Message = null);
