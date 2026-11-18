using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IAnulacionApiService
{
    Task<AutorizarAnulacionResponse> AutorizarAsync(long expectedGeneration, AutorizarAnulacionRequest request, CancellationToken cancellationToken = default);
    Task<ResultadoAnulacionDto> EjecutarAsync(long expectedGeneration, int idPedido, EjecutarAnulacionRequest request, CancellationToken cancellationToken = default);
    Task<OperacionAnulacionDto> ConsultarAsync(Guid idOperacion, CancellationToken cancellationToken = default);
    Task<OperacionAnulacionDto> BarreraAsync(long expectedGeneration, Guid idOperacion, bool intervencionAjena, CancellationToken cancellationToken = default);
}

/// <summary>
/// Authorize, execute and barrier are one-emission writes: no refresh or automatic retry.
/// The ephemeral token lives only in memory for the duration of the call.
/// </summary>
public sealed class AnulacionApiService(ISessionManager session) : IAnulacionApiService
{
    public async Task<AutorizarAnulacionResponse> AutorizarAsync(
        long expectedGeneration, AutorizarAnulacionRequest request, CancellationToken cancellationToken = default)
    {
        if (expectedGeneration <= 0 || session.CurrentSession?.IdUsuario is not int actor ||
            session is not IAnulacionSessionSender sender)
            throw new SessionExpiredException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await sender.SendAnulacionOnceAsync(actor, expectedGeneration,
            () => new HttpRequestMessage(HttpMethod.Post, "api/seguridad/autorizar-anulacion")
            {
                Content = JsonContent.Create(request)
            }, timeout.Token);
        var result = await MesaApiResponse.ReadAsync<AutorizarAnulacionResponse>(response, timeout.Token);
        if (string.IsNullOrWhiteSpace(result.Token) || result.Token.Length > 256 ||
            result.ExpiraUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("La API devolvió una autorización superior inválida.");
        return result;
    }

    public async Task<ResultadoAnulacionDto> EjecutarAsync(
        long expectedGeneration, int idPedido, EjecutarAnulacionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (expectedGeneration <= 0 || idPedido <= 0 || session.CurrentSession?.IdUsuario is not int actor ||
            session is not IAnulacionSessionSender sender) throw new SessionExpiredException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await sender.SendAnulacionOnceAsync(actor, expectedGeneration,
            () => new HttpRequestMessage(HttpMethod.Post, $"api/pedidos/{idPedido}/anulaciones")
            {
                Content = JsonContent.Create(request)
            }, timeout.Token);
        var result = await MesaApiResponse.ReadAsync<ResultadoAnulacionDto>(response, timeout.Token);
        if (result.Estado is not ("Completada" or "Rechazada") || string.IsNullOrWhiteSpace(result.Codigo))
            throw new InvalidDataException("La API devolvió un resultado de anulación inválido.");
        return result;
    }

    public async Task<OperacionAnulacionDto> ConsultarAsync(
        Guid idOperacion, CancellationToken cancellationToken = default)
    {
        if (idOperacion == Guid.Empty) throw new ArgumentException("Operación inválida.", nameof(idOperacion));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await session.SendHumanAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"api/anulaciones/operaciones/{idOperacion:D}"),
            true, timeout.Token);
        return await LeerOperacionAsync(response, timeout.Token);
    }

    public async Task<OperacionAnulacionDto> BarreraAsync(
        long expectedGeneration, Guid idOperacion, bool intervencionAjena,
        CancellationToken cancellationToken = default)
    {
        if (expectedGeneration <= 0 || idOperacion == Guid.Empty ||
            session.CurrentSession?.IdUsuario is not int actor ||
            session is not IAnulacionSessionSender sender) throw new SessionExpiredException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await sender.SendAnulacionOnceAsync(actor, expectedGeneration,
            () => new HttpRequestMessage(HttpMethod.Post, $"api/anulaciones/operaciones/{idOperacion:D}/cancelacion")
            {
                Content = JsonContent.Create(new { confirmaIntervencion = true, intervencionAjena })
            }, timeout.Token);
        return await LeerOperacionAsync(response, timeout.Token);
    }

    private static async Task<OperacionAnulacionDto> LeerOperacionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await MesaApiResponse.ReadAsync<OperacionAnulacionDto>(response, cancellationToken);
        if (result.Estado is not ("Autorizada" or "Completada" or "Rechazada" or "Cancelada") ||
            string.IsNullOrWhiteSpace(result.Codigo))
            throw new InvalidDataException("La API devolvió un estado de operación inválido.");
        if (result.Resultado is { Length: > 8192 }) throw new InvalidDataException("Resultado de operación inválido.");
        return result;
    }

    internal static ResultadoAnulacionDto? ParseResultado(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) return null;
        try
        {
            return JsonSerializer.Deserialize<ResultadoAnulacionDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
