using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface ICocinaApiService
{
    Task<CocinaPaginaDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default);
    Task<CocinaComandaDto> CambiarEstadoAsync(CocinaComandaDto comanda, CancellationToken cancellationToken = default);
}

public sealed class CocinaApiService(ISessionManager session) : ICocinaApiService
{
    public async Task<CocinaPaginaDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (cursor is { Length: 0 or > 256 }) throw new ArgumentException("Cursor inválido.", nameof(cursor));
        var path = "api/cocina/cola?limite=25";
        if (cursor is not null) path += $"&cursor={Uri.EscapeDataString(cursor)}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, path), timeout.Token);
        var page = await MesaApiResponse.ReadAsync<CocinaPaginaDto>(response, timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        if (page.Items is null || page.Items.Count > 25 || page.Items.Any(item => !EsValida(item, false)) ||
            page.Items.Select(item => item.IdComanda).Distinct().Count() != page.Items.Count ||
            page.CursorSiguiente is { Length: 0 or > 256 } || page.CursorSiguiente is not null && page.Items.Count != 25 ||
            page.Items.Zip(page.Items.Skip(1)).Any(pair => Comparar(pair.First, pair.Second) >= 0))
            throw new InvalidDataException("La API devolvió una cola inválida.");
        return page;
    }

    public async Task<CocinaComandaDto> CambiarEstadoAsync(CocinaComandaDto comanda, CancellationToken cancellationToken = default)
    {
        if (!EsValida(comanda, false)) throw new ArgumentException("Comanda inválida.", nameof(comanda));
        var estado = comanda.Estado == "Abierta" ? "En Preparación" : "Lista";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await session.SendHumanAsync(() => new HttpRequestMessage(HttpMethod.Put,
            $"api/cocina/comandas/{comanda.IdComanda}/estado")
        {
            Content = JsonContent.Create(new { Estado = estado, VersionComanda = comanda.Version })
        }, false, timeout.Token);
        var result = await MesaApiResponse.ReadAsync<CocinaComandaDto>(response, timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        if (!EsValida(result, true) || result.IdComanda != comanda.IdComanda || result.IdPedido != comanda.IdPedido ||
            result.IdMesa != comanda.IdMesa || result.NumeroMesa != comanda.NumeroMesa || result.IdGrupo != comanda.IdGrupo ||
            result.IdUsuario != comanda.IdUsuario || result.FechaHora != comanda.FechaHora || result.Estado != estado ||
            !result.Lineas.SequenceEqual(comanda.Lineas))
            throw new InvalidDataException("La confirmación de Cocina no corresponde a la solicitud.");
        return result;
    }

    private static bool EsValida(CocinaComandaDto? item, bool permiteLista) =>
        item is not null && item.IdComanda > 0 && item.IdPedido > 0 && item.IdUsuario > 0 &&
        (item.IdMesa is > 0 && item.NumeroMesa is > 0 && item.IdGrupo is null ||
         item.IdGrupo is > 0 && item.IdMesa is null && item.NumeroMesa is null) &&
        item.FechaHora != default && item.FechaHora.Offset == TimeSpan.Zero &&
        (item.Estado is "Abierta" or "En Preparación" || permiteLista && item.Estado == "Lista") &&
        item.Lineas is { Count: > 0 and <= 50 } && item.Lineas.All(line => line is not null &&
            line.IdDetalle > 0 && !string.IsNullOrWhiteSpace(line.NombreProducto) && line.Cantidad is >= 1 and <= 99 && line.Notas is not null) &&
        item.Lineas.Select(line => line.IdDetalle).Distinct().Count() == item.Lineas.Count;

    private static int Comparar(CocinaComandaDto left, CocinaComandaDto right)
    {
        var fecha = left.FechaHora.CompareTo(right.FechaHora);
        return fecha != 0 ? fecha : left.IdComanda.CompareTo(right.IdComanda);
    }
}
