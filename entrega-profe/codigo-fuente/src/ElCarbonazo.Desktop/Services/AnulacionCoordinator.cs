using System.Net;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public enum EstadoResultadoAnulacion
{
    Completada,
    Rechazada,
    Incierta,
    NoDisponible
}

public sealed record ResultadoAnulacion(EstadoResultadoAnulacion Estado, string Codigo, string Mensaje)
{
    public bool EsTerminal => Estado is EstadoResultadoAnulacion.Completada or EstadoResultadoAnulacion.Rechazada;
}

/// <summary>
/// Serializa una única intención de anulación, conserva el diario antes de enviar y nunca reejecuta
/// durante la recuperación. El token efímero no se persiste ni se registra.
/// </summary>
public sealed class AnulacionCoordinator(
    AnulacionJournal journal,
    IAnulacionApiService api,
    Func<int?> actor,
    Func<long> generation,
    Func<bool> esJefa)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AnulacionJournalState? Estado() => journal.Read();

    public async Task<ResultadoAnulacion> AnularAsync(
        IntencionAnulacion intencion, string email, string password, CancellationToken cancellationToken = default)
    {
        if (intencion.IdOperacion == Guid.Empty || intencion.IdPedido <= 0 || intencion.IdComanda <= 0 ||
            NormalizarMotivo(intencion.Motivo) is null || string.IsNullOrWhiteSpace(email) ||
            email.Trim().Length > 254 || string.IsNullOrEmpty(password) || password.Length > 256)
            return new(EstadoResultadoAnulacion.NoDisponible, "VALIDATION_ERROR",
                "Complete el motivo, el correo de Jefa y su contraseña antes de confirmar.");
        var currentActor = actor();
        if (currentActor is not > 0)
            return new(EstadoResultadoAnulacion.NoDisponible, "AUTH_SESION_EXPIRADA",
                "La sesión no está disponible. Vuelva a iniciar sesión.");
        // La generación se captura antes de registrar el diario: un cambio de sesión posterior
        // impide emitir y deja la operación pendiente para recuperación explícita.
        var sessionGeneration = generation();
        if (sessionGeneration <= 0)
            return new(EstadoResultadoAnulacion.NoDisponible, "AUTH_SESION_EXPIRADA",
                "La sesión no está disponible. Vuelva a iniciar sesión.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = journal.Read();
            if (state is null)
                return new(EstadoResultadoAnulacion.NoDisponible, "DIARIO_NO_DISPONIBLE",
                    "La anulación no está disponible: falta el diario protegido o no se puede verificar.");
            if (state.Estado != "SinPendientes")
                return new(EstadoResultadoAnulacion.NoDisponible, "OPERACION_PENDIENTE",
                    "Ya existe una anulación pendiente. Consulte esa operación antes de iniciar otra.");
            if (!journal.Begin(intencion.IdOperacion, currentActor.Value))
                return new(EstadoResultadoAnulacion.NoDisponible, "DIARIO_NO_ESCRITO",
                    "No se pudo registrar la intención en el diario local; no se envió nada.");

            try
            {
                var authorization = await api.AutorizarAsync(sessionGeneration, new AutorizarAnulacionRequest(
                    intencion.IdOperacion, intencion.IdPedido, intencion.IdComanda, intencion.IdDetalle,
                    intencion.Motivo, email.Trim(), password), cancellationToken);
                if (generation() != sessionGeneration)
                    return Incierta("AUTH_CAMBIO_SESION",
                        "La sesión cambió antes de ejecutar. No se ejecutó la anulación; consulte la operación pendiente.");
                var result = await api.EjecutarAsync(sessionGeneration, intencion.IdPedido, new EjecutarAnulacionRequest(
                    intencion.IdOperacion, intencion.IdComanda, intencion.IdDetalle, intencion.Motivo,
                    authorization.Token), cancellationToken);
                journal.MarkTerminal(intencion.IdOperacion, currentActor.Value);
                return result.Estado == "Completada"
                    ? new(EstadoResultadoAnulacion.Completada, result.Codigo, DescribirCompletada(result))
                    : new(EstadoResultadoAnulacion.Rechazada, result.Codigo, DescribirRechazo(result.Codigo));
            }
            catch (SessionExpiredException)
            {
                // El transporte rechazó la emisión antes de enviarla; la intención queda pendiente
                // porque no puede distinguirse aquí si una autorización llegó al servidor.
                return Incierta("AUTH_SESION_EXPIRADA",
                    "La sesión expiró o cambió durante la anulación. No se confirmó efecto; consulte la operación pendiente.");
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Incierta("AUTH_SESION_EXPIRADA",
                    "La sesión expiró durante la anulación. No se confirmó efecto; consulte la operación pendiente.");
            }
            catch (ApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                var code = ex.Problem?.Code ?? "ANULACION_RECHAZADA";
                journal.MarkTerminal(intencion.IdOperacion, currentActor.Value);
                return new(EstadoResultadoAnulacion.Rechazada, code, DescribirRechazo(code));
            }
            catch (Exception ex) when (EsIncierta(ex))
            {
                return Incierta("RESULTADO_INCIERTO",
                    "Resultado incierto: la anulación pudo aplicarse. No repita el envío ni cierre la aplicación. " +
                    "Consulte la operación pendiente o establezca una barrera.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ResultadoAnulacion> RecuperarAsync(
        bool establecerBarrera, CancellationToken cancellationToken = default)
    {
        var currentActor = actor();
        if (currentActor is not > 0)
            return new(EstadoResultadoAnulacion.NoDisponible, "AUTH_SESION_EXPIRADA",
                "La sesión no está disponible. Vuelva a iniciar sesión.");
        var sessionGeneration = generation();
        if (sessionGeneration <= 0)
            return new(EstadoResultadoAnulacion.NoDisponible, "AUTH_SESION_EXPIRADA",
                "La sesión no está disponible. Vuelva a iniciar sesión.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = journal.Read();
            if (state is not { Operacion: Guid operation, Propietario: int owner })
                return new(EstadoResultadoAnulacion.NoDisponible, "SIN_OPERACION_PENDIENTE",
                    "No hay una operación de anulación pendiente en este equipo.");
            if (owner != currentActor && !esJefa())
                return new(EstadoResultadoAnulacion.NoDisponible, "INTERVENCION_REQUERIDA",
                    "La operación pertenece a otro usuario. Se requiere una Jefa para intervenirla.");
            var ajena = owner != currentActor;
            if (state.Estado == "TerminalPorConciliar")
                return new(EstadoResultadoAnulacion.Completada, "PENDIENTE_CONCILIACION",
                    "La operación ya es terminal. Actualice la cuenta para conciliar el diario.");

            try
            {
                var consult = await api.ConsultarAsync(operation, cancellationToken);
                if (consult.Estado is "Completada" or "Rechazada" or "Cancelada")
                {
                    journal.MarkTerminal(operation, owner);
                    return consult.Estado == "Completada"
                        ? new(EstadoResultadoAnulacion.Completada, consult.Codigo,
                            "La operación consta como completada. Actualice la cuenta para conciliar.")
                        : new(EstadoResultadoAnulacion.Rechazada, consult.Codigo,
                            consult.Estado == "Cancelada"
                                ? "La operación quedó cancelada por barrera; no tuvo efecto de negocio."
                                : DescribirRechazo(consult.Codigo));
                }
                if (!establecerBarrera)
                    return Incierta("OPERACION_AUTORIZADA_SIN_EFECTO",
                        "La operación está autorizada pero no consta ejecutada; el token no se conserva. " +
                        "Confirme la barrera para impedir una ejecución tardía.");
                return await BarreraAsync(sessionGeneration, operation, owner, ajena, cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                if (!establecerBarrera)
                    return Incierta("SIN_OPERACION_CONFIRMADA",
                        "La API no encontró la operación. Confirme la barrera antes de continuar.");
                return await BarreraAsync(sessionGeneration, operation, owner, ajena, cancellationToken);
            }
            catch (SessionExpiredException)
            {
                return Incierta("AUTH_SESION_EXPIRADA",
                    "La sesión expiró o cambió. No se confirmó el estado de la operación; vuelva a iniciar sesión.");
            }
            catch (Exception ex) when (EsIncierta(ex))
            {
                return Incierta("CONSULTA_INCIERTA",
                    "No se pudo confirmar el estado de la operación. No repita la anulación; reintente la consulta.");
            }
            catch (ApiException ex)
            {
                var code = ex.Problem?.Code ?? "OPERACION_NO_DISPONIBLE";
                return new(EstadoResultadoAnulacion.NoDisponible, code, DescribirRechazo(code));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool Conciliar(Guid operation)
    {
        var state = journal.Read();
        if (state is not { Estado: "TerminalPorConciliar", Operacion: Guid current, Propietario: int owner } ||
            current != operation)
            return false;
        return journal.ClearAfterReconciliation(operation, owner);
    }

    public bool OperacionAjena()
    {
        var state = journal.Read();
        var current = actor();
        return state is { Estado: not "SinPendientes", Propietario: int owner } &&
            current is > 0 && owner != current;
    }

    private async Task<ResultadoAnulacion> BarreraAsync(
        long sessionGeneration, Guid operation, int owner, bool ajena, CancellationToken cancellationToken)
    {
        var result = await api.BarreraAsync(sessionGeneration, operation, ajena, cancellationToken);
        journal.MarkTerminal(operation, owner);
        // La barrera se serializa con la ejecución: si la ejecución ya ganó, su resultado es el vigente.
        if (result.Estado == "Completada")
            return new(EstadoResultadoAnulacion.Completada, result.Codigo,
                "La ejecución ya había ganado: la anulación se completó y no puede revertirse. " +
                "Actualice la cuenta para conciliar.");
        if (result.Estado == "Rechazada")
            return new(EstadoResultadoAnulacion.Rechazada, result.Codigo, DescribirRechazo(result.Codigo));
        return new(EstadoResultadoAnulacion.Rechazada, result.Codigo,
            "Barrera establecida: la anulación no se ejecutará. No se modificaron pedido ni inventario.");
    }

    private static ResultadoAnulacion Incierta(string code, string message) =>
        new(EstadoResultadoAnulacion.Incierta, code, message);

    private static bool EsIncierta(Exception ex) => ex is HttpRequestException or IOException or JsonException
        or InvalidDataException or OperationCanceledException or SecurityConnectionException or SessionPausedException
        || ex is ApiException { StatusCode: >= HttpStatusCode.InternalServerError };

    private static string DescribirCompletada(ResultadoAnulacionDto result)
    {
        var monto = result.MontoRevertido is decimal value ? $" Importe anulado: C$ {value:N2}." : "";
        return $"Anulación completada y registrada. El inventario se devolvió una sola vez.{monto} " +
               "Actualice la cuenta para conciliar el resultado.";
    }

    internal static string DescribirRechazo(string code) => code switch
    {
        "CUENTA_YA_PAGADA" => "La cuenta ya fue pagada o tiene un cobro registrado; no se anuló.",
        "OPERACION_NO_TERMINAL" => "Hay una operación de cobro preparada o no terminal. Revise Caja antes de anular.",
        "ITEM_YA_CANCELADO" => "La línea ya estaba anulada; no se modificó nada.",
        "EVIDENCIA_CONSUMO_INSUFICIENTE" =>
            "No hay evidencia suficiente para devolver exactamente el inventario. Se rechazó sin cambios.",
        "OPERACION_CLAVE_EN_USO" => "La operación ya pertenece a otra intención o a otro usuario.",
        "TOKEN_SUPERIOR_EXPIRADO_O_USADO" =>
            "La autorización de Jefa expiró o ya fue usada. No se ejecutó la anulación; solicítela de nuevo.",
        "AUTORIZACION_SUPERIOR_INVALIDA" => "La contraseña de Jefa no es válida o la cuenta no está habilitada.",
        "DEMASIADOS_INTENTOS_AUTORIZACION" => "Demasiados intentos de autorización. Espere cinco minutos.",
        "AUTORIZACION_SUPERIOR_NO_DISPONIBLE" =>
            "La autorización superior no está disponible. Contacte al administrador; no se modificó nada.",
        "AUTH_PROHIBIDO" => "Su rol no permite anular esta cuenta.",
        "AUTH_SESION_EXPIRADA" => "La sesión expiró. Vuelva a iniciar sesión.",
        "DESTINO_STATE_CONFLICT" => "La mesa, el grupo o la comanda cambiaron. Actualice y reintente.",
        "VERSION_CONFLICT" => "El inventario cambió durante la operación. Actualice y reintente.",
        "VALIDATION_ERROR" => "Los datos de la anulación no son válidos.",
        "RECURSO_NO_ENCONTRADO" => "La API no encontró la operación o el recurso.",
        _ => "La anulación fue rechazada sin cambios. Revise la cuenta antes de reintentar."
    };

    internal static string? NormalizarMotivo(string? value)
    {
        if (value is null) return null;
        try { value = value.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { return null; }
        return value.EnumerateRunes().Count() is >= 5 and <= 250 && !value.EnumerateRunes().Any(Rune.IsControl)
            ? value
            : null;
    }
}
