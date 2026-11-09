using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public interface ICuentasService
{
    Task<CuentaActivaDto?> GetActivaAsync(int? mesaId, int? grupoId, CancellationToken cancellationToken);
}
