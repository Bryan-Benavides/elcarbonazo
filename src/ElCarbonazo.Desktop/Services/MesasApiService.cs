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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await session.SendPollingAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "api/mesas"), timeout.Token);
        var mesas = await MesaApiResponse.ReadAsync<List<MesaDto>>(response, timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        if (mesas.Any(m => m is null || !MesaApiResponse.EsMesaValida(m)) ||
            mesas.Select(m => m.IdMesa).Distinct().Count() != mesas.Count ||
            mesas.Select(m => m.Numero).Distinct().Count() != mesas.Count)
            throw new InvalidDataException("La API devolvió mesas inválidas o duplicadas.");
        return mesas;
    }

    public async Task<MesaDto> UpdateEstadoAsync(int id, string estado, uint version, CancellationToken cancellationToken = default)
    {
        using var response = await session.SendHumanAsync(
            () => new HttpRequestMessage(HttpMethod.Put, $"api/mesas/{id}/estado")
            {
                Content = JsonContent.Create(new UpdateMesaEstadoRequest(estado, version))
            }, false, cancellationToken);
        var mesa = await MesaApiResponse.ReadAsync<MesaDto>(response, cancellationToken);
        if (!MesaApiResponse.EsMesaValida(mesa) || mesa.IdMesa != id || mesa.Estado != estado || mesa.IdGrupo is not null)
            throw new InvalidDataException("La API devolvió una mesa incompatible con el cambio solicitado.");
        return mesa;
    }
}

internal static class MesaApiResponse
{
    public static bool EsMesaValida(MesaDto mesa) => mesa.IdMesa > 0 && mesa.Numero > 0 && mesa.Capacidad > 0 &&
        (mesa.Estado is "Libre" or "Ocupada" or "Reservada" && mesa.IdGrupo is null ||
         mesa.Estado == "Agrupada" && mesa.IdGrupo > 0);

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
