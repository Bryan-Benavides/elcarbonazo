using System.Globalization;
using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IReservacionesApiService
{
    Task<IReadOnlyList<DisponibilidadMesaDto>> DisponibilidadAsync(DateTimeOffset desde, DateTimeOffset hasta,
        int personas, CancellationToken ct);
    Task<IReadOnlyList<ReservacionDto>> ListarAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct);
    Task<ReservacionDto> CrearAsync(CrearReservacionRequest request, CancellationToken ct);
    Task<ReservacionDto> CancelarAsync(int id, CancellationToken ct);
}

public sealed class ReservacionesApiService(ISessionManager session) : IReservacionesApiService
{
    public async Task<IReadOnlyList<DisponibilidadMesaDto>> DisponibilidadAsync(
        DateTimeOffset desde, DateTimeOffset hasta, int personas, CancellationToken ct)
    {
        var path = $"api/reservaciones/disponibilidad?desdeUtc={Q(desde)}&hastaUtc={Q(hasta)}&personas={personas}";
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct);
        var items = await MesaApiResponse.ReadAsync<List<DisponibilidadMesaDto>>(response, ct);
        if (items.Count > 15 || items.Any(x => x.IdMesa <= 0 || x.Numero is < 1 or > 15 || x.Capacidad < personas) ||
            items.Select(x => x.IdMesa).Distinct().Count() != items.Count)
            throw new InvalidDataException("La API devolvió disponibilidad inválida.");
        return items;
    }

    public async Task<IReadOnlyList<ReservacionDto>> ListarAsync(
        DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var path = $"api/reservaciones?desdeUtc={Q(desde)}&hastaUtc={Q(hasta)}";
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct);
        var items = await MesaApiResponse.ReadAsync<List<ReservacionDto>>(response, ct);
        if (items.Count > 500 || items.Any(x => !Valida(x)) ||
            items.Select(x => x.IdReservacion).Distinct().Count() != items.Count)
            throw new InvalidDataException("La API devolvió reservaciones inválidas.");
        return items;
    }

    public async Task<ReservacionDto> CrearAsync(CrearReservacionRequest request, CancellationToken ct)
    {
        if (session.CurrentSession?.IdUsuario is not int actor || session is not IReservacionesSessionSender sender)
            throw new SessionExpiredException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await sender.SendReservacionOnceAsync(actor, sender.ReservacionesSessionGeneration,
            () => new HttpRequestMessage(HttpMethod.Post, "api/reservaciones") { Content = JsonContent.Create(request) },
            timeout.Token);
        var result = await MesaApiResponse.ReadAsync<ReservacionDto>(response, timeout.Token);
        if (!Valida(result) || result.IdMesa != request.IdMesa || result.Estado != "Activa")
            throw new InvalidDataException("La API devolvió una reservación incompatible.");
        return result;
    }

    public async Task<ReservacionDto> CancelarAsync(int id, CancellationToken ct)
    {
        if (id <= 0 || session.CurrentSession?.IdUsuario is not int actor ||
            session is not IReservacionesSessionSender sender) throw new SessionExpiredException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await sender.SendReservacionOnceAsync(actor, sender.ReservacionesSessionGeneration,
            () => new HttpRequestMessage(HttpMethod.Post, $"api/reservaciones/{id}/cancelar"), timeout.Token);
        var result = await MesaApiResponse.ReadAsync<ReservacionDto>(response, timeout.Token);
        if (!Valida(result) || result.IdReservacion != id || result.Estado != "Cancelada")
            throw new InvalidDataException("La API devolvió una cancelación incompatible.");
        return result;
    }

    private static bool Valida(ReservacionDto x) => x.IdReservacion > 0 && x.IdMesa > 0 &&
        x.NumeroMesa is >= 1 and <= 15 && !string.IsNullOrWhiteSpace(x.NombreReferencia) &&
        x.NombreReferencia.Length <= 60 && x.CantidadPersonas is >= 1 and <= 20 &&
        x.InicioUtc.Offset == TimeSpan.Zero && x.FinUtc.Offset == TimeSpan.Zero && x.InicioUtc < x.FinUtc &&
        x.FinUtc - x.InicioUtc <= TimeSpan.FromHours(8) && x.CreadaUtc.Offset == TimeSpan.Zero &&
        x.Estado is "Activa" or "Cancelada";

    private static string Q(DateTimeOffset value) => Uri.EscapeDataString(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}
