using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ElCarbonazo.Api.Domain;

// Internal representation only: HTTP contracts and execution belong to later C2/C3 work.
public sealed record CajaIntencionCanonica
{
    internal CajaIntencionCanonica(string tipo, short version, string argumentosJson, string hashArgumentos) =>
        (Tipo, Version, ArgumentosJson, HashArgumentos) = (tipo, version, argumentosJson, hashArgumentos);

    public string Tipo { get; }
    public short Version { get; }
    public string ArgumentosJson { get; }
    public string HashArgumentos { get; }
}

public static class CajaOperacionCanonica
{
    private const decimal MaxMonto = 9999999999.99m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CajaIntencionCanonica? Abrir(decimal fondoInicial, bool confirmado)
    {
        if (!MontoValido(fondoInicial) || !confirmado) return null;
        return Crear("AbrirTurno", new { fondoInicial = NormalizarMonto(fondoInicial), confirmado });
    }

    public static CajaIntencionCanonica? Cobrar(int pedidoId, int turnoId, decimal totalVisto, string? huellaLineas,
        string? metodo, decimal? efectivoRecibido, string? referencia, bool verificacionExterna, bool referenciaRepetida)
    {
        if (pedidoId <= 0 || turnoId <= 0 || !MontoValido(totalVisto) || totalVisto == 0 ||
            !EsHash(huellaLineas)) return null;
        string? referenciaNormalizada = null;
        if (metodo == CajaStates.Efectivo)
        {
            if (efectivoRecibido is null || !MontoValido(efectivoRecibido.Value) || referencia is not null ||
                verificacionExterna || referenciaRepetida) return null;
        }
        else if (metodo is CajaStates.Tarjeta or CajaStates.Transferencia)
        {
            if (efectivoRecibido is not null || !verificacionExterna || !TextoValido(referencia, 100, out referenciaNormalizada)) return null;
        }
        else return null;

        return Crear("CobrarCuenta", new
        {
            pedidoId,
            turnoId,
            totalVisto = NormalizarMonto(totalVisto),
            huellaLineas,
            metodo,
            efectivoRecibido = efectivoRecibido is null ? (decimal?)null : NormalizarMonto(efectivoRecibido.Value),
            referencia = referenciaNormalizada,
            verificacionExterna,
            referenciaRepetida
        });
    }

    public static CajaIntencionCanonica? Cerrar(int turnoId, decimal efectivoContado, string? motivo, bool confirmado)
    {
        if (turnoId <= 0 || !MontoValido(efectivoContado) || !confirmado) return null;
        string? motivoNormalizado = null;
        if (motivo is not null && !TextoValido(motivo, 500, out motivoNormalizado)) return null;
        return Crear("CerrarTurno", new { turnoId, efectivoContado = NormalizarMonto(efectivoContado), motivo = motivoNormalizado, confirmado });
    }

    private static CajaIntencionCanonica Crear<T>(string tipo, T argumentos)
    {
        var json = JsonSerializer.Serialize(argumentos, JsonOptions);
        // Include the contract version and type in the digest; operation UUID and actor are separate.
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { tipo, version = 1, argumentos }, JsonOptions);
        return new CajaIntencionCanonica(tipo, 1, json, Convert.ToHexStringLower(SHA256.HashData(canonical)));
    }

    private static bool MontoValido(decimal value) => value >= 0 && value <= MaxMonto && decimal.Round(value, 2) == value;
    private static decimal NormalizarMonto(decimal value) => decimal.Parse(value.ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    private static bool EsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TextoValido(string? value, int max, out string? normalized)
    {
        normalized = null;
        if (value is null) return false;
        if (value.EnumerateRunes().Any(Rune.IsControl)) return false;
        try { normalized = value.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { return false; }
        var runes = normalized.EnumerateRunes().ToArray();
        return runes.Length > 0 && runes.Length <= max && !runes.Any(Rune.IsControl);
    }
}
