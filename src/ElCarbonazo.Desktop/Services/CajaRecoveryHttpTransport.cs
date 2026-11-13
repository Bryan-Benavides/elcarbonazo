using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

public sealed class CajaRecoveryHttpTransport(ICajaSessionSender session) : ICajaRecoveryTransport
{
    public async Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
    {
        if (arguments is null) return CajaRecoveryOutcome.Uncertain;
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Put, $"api/caja/operaciones/{operation:D}")
        {
            Content = JsonContent.Create(new { tipo = type, version = 1, argumentos = arguments })
        }, false, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken)
    {
        using var response = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/operaciones/{operation:D}"), true, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, CancellationToken cancellationToken)
    {
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Post, $"api/caja/operaciones/{operation:D}/cancelacion")
        {
            Content = JsonContent.Create(new { confirmaIntervencion = false })
        }, false, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
    {
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Post, $"api/caja/operaciones/{operation:D}/ejecucion")
        {
            Content = JsonContent.Create(new { })
        }, false, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    // C3 business endpoints do not exist yet: no ordinary GET can unlock this journal.
    public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) => Task.FromResult(false);

    private static async Task<CajaRecoveryOutcome> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return CajaRecoveryOutcome.NotFound;
        if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return CajaRecoveryOutcome.Uncertain;
        if (response.Content.Headers.ContentLength > 16384) return CajaRecoveryOutcome.Uncertain;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[16385];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > 16384) return CajaRecoveryOutcome.Uncertain;
        using var json = JsonDocument.Parse(bytes.AsMemory(0, count));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return CajaRecoveryOutcome.Uncertain;
        string? state = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "estado") continue;
            if (state is not null || property.Value.ValueKind != JsonValueKind.String) return CajaRecoveryOutcome.Uncertain;
            state = property.Value.GetString();
        }
        return state switch
        {
            "Preparada" when response.IsSuccessStatusCode => CajaRecoveryOutcome.Prepared,
            "Completada" when response.IsSuccessStatusCode => CajaRecoveryOutcome.Completed,
            "Cancelada" when response.IsSuccessStatusCode => CajaRecoveryOutcome.Cancelled,
            "Rechazada" when (int)response.StatusCode is >= 400 and < 500 => CajaRecoveryOutcome.Rejected,
            _ => CajaRecoveryOutcome.Uncertain
        };
    }
}
