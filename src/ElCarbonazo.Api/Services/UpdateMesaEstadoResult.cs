using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public enum UpdateMesaEstadoOutcome
{
    Success,
    InvalidState,
    InvalidTransition,
    NotFound,
    PendingOrder,
    GroupedConflict,
    ConcurrencyConflict
}

public sealed record UpdateMesaEstadoResult(
    UpdateMesaEstadoOutcome Outcome,
    MesaDto? Mesa = null,
    string? Message = null);
