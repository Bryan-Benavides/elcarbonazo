using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public interface IGruposMesaService
{
    Task<GruposMesaResult> CreateAsync(
        CreateGrupoMesaRequest request,
        int userId,
        CancellationToken cancellationToken);

    Task<GruposMesaResult> CloseAsync(int groupId, int userId, CancellationToken cancellationToken);
}
