namespace ElCarbonazo.Api.Services;

internal sealed class CajaAutorizacionRevocadaException(int status, string code) : Exception
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
