using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public interface IMesasService
{
    Task<IReadOnlyList<MesaDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<UpdateMesaEstadoResult> UpdateEstadoAsync(
        int id,
        UpdateMesaEstadoRequest request,
        CancellationToken cancellationToken);
}
