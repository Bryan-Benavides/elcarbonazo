using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IMesasApiService
{
    Task<IReadOnlyList<MesaDto>> GetAsync(CancellationToken cancellationToken = default);
    Task<MesaDto> UpdateEstadoAsync(int id, string estado, uint version, CancellationToken cancellationToken = default);
}

public sealed class MesasApiService(ISessionManager session) : IMesasApiService
{
    public async Task<IReadOnlyList<MesaDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await session.SendPollingAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "api/mesas"), cancellationToken);
        return await MesaApiResponse.ReadAsync<List<MesaDto>>(response, cancellationToken);
    }

    public async Task<MesaDto> UpdateEstadoAsync(int id, string estado, uint version, CancellationToken cancellationToken = default)
    {
        using var response = await session.SendHumanAsync(
            () => new HttpRequestMessage(HttpMethod.Put, $"api/mesas/{id}/estado")
            {
                Content = JsonContent.Create(new UpdateMesaEstadoRequest(estado, version))
            }, false, cancellationToken);
        return await MesaApiResponse.ReadAsync<MesaDto>(response, cancellationToken);
    }
}

internal static class MesaApiResponse
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            // El estado HTTP sigue siendo válido aunque el cuerpo no lo sea.
        }
        throw new ApiException(response.StatusCode, problem);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("La API devolvió una respuesta vacía o inválida.");
    }
}
