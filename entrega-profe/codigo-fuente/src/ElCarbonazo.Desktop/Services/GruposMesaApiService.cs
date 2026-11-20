using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IGruposMesaApiService
{
    Task<GrupoMesaDto> CreateAsync(IReadOnlyList<MesaVersionRequest> mesas, CancellationToken cancellationToken = default);
    Task CloseAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class GruposMesaApiService(ISessionManager session) : IGruposMesaApiService
{
    public async Task<GrupoMesaDto> CreateAsync(IReadOnlyList<MesaVersionRequest> mesas, CancellationToken cancellationToken = default)
    {
        using var response = await session.SendHumanAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "api/grupos-mesa")
            {
                Content = JsonContent.Create(new CreateGrupoMesaRequest(mesas))
            }, false, cancellationToken);
        return await MesaApiResponse.ReadAsync<GrupoMesaDto>(response, cancellationToken);
    }

    public async Task CloseAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await session.SendHumanAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, $"api/grupos-mesa/{id}"),
            false, cancellationToken);
        await MesaApiResponse.EnsureSuccessAsync(response, cancellationToken);
    }
}
