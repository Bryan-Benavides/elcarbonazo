namespace ElCarbonazo.Api.Services;

public sealed class CajaEjecutorDominio(CajaAperturaExecutor apertura, CajaCierreExecutor cierre, CajaCobroExecutor cobro) : ICajaEjecutorDominio
{
    public bool PuedeEjecutar(string tipo) => apertura.PuedeEjecutar(tipo) || cierre.PuedeEjecutar(tipo) || cobro.PuedeEjecutar(tipo);
    public Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken) =>
        operacion.Tipo switch
        {
            "AbrirTurno" => apertura.EjecutarAsync(operacion, cancellationToken),
            "CerrarTurno" => cierre.EjecutarAsync(operacion, cancellationToken),
            "CobrarCuenta" => cobro.EjecutarAsync(operacion, cancellationToken),
            _ => throw new InvalidOperationException("Contrato no soportado.")
        };
}
