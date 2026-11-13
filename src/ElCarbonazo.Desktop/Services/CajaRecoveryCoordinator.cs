namespace ElCarbonazo.Desktop.Services;

public enum CajaRecoveryOutcome { Prepared, NotFound, Completed, Rejected, Cancelled, Uncertain }

// Transport must use the specified actor's current authenticated session and must never retry writes.
public interface ICajaRecoveryTransport
{
    Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken);
    Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken);
    Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken);
    Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, CancellationToken cancellationToken);
    Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken);
}

// The application wires this coordinator to REST; C3/C4 must supply typed requests and business reconciliation.
public sealed class CajaRecoveryCoordinator(CajaRecoveryStore store, ICajaRecoveryTransport transport, Func<int?> currentActor)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _observedPrepared;

    public async Task<CajaRecoveryOutcome> BeginAsync(Guid operation, int actor, string type, CancellationToken cancellationToken = default, object? arguments = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (currentActor() != actor || !store.Begin(operation, actor, type)) return CajaRecoveryOutcome.Uncertain;
            if (currentActor() != actor) return CajaRecoveryOutcome.Uncertain;
            try
            {
                var outcome = await transport.PrepareAsync(operation, actor, type, arguments, cancellationToken);
                return RecordOutcome(operation, actor, outcome, allowExecution: true);
            }
            catch (Exception ex) when (IsUncertain(ex))
            {
                return CajaRecoveryOutcome.Uncertain;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<CajaRecoveryOutcome> RecoverAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = store.Read();
            if (state?.Operacion is not Guid id || state.Propietario is not int owner || currentActor() != owner)
                return CajaRecoveryOutcome.Uncertain;
            _observedPrepared = null;
            try
            {
                var outcome = await transport.QueryAsync(id, owner, cancellationToken);
                return RecordOutcome(id, owner, outcome);
            }
            catch (Exception ex) when (IsUncertain(ex))
            {
                return CajaRecoveryOutcome.Uncertain;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<CajaRecoveryOutcome> CancelAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = store.Read();
            if (state?.Estado != "Pendiente" || state.Operacion is not Guid id || state.Propietario is not int owner || currentActor() != owner)
                return CajaRecoveryOutcome.Uncertain;
            _observedPrepared = null;
            try { return RecordOutcome(id, owner, await transport.CancelAsync(id, owner, cancellationToken)); }
            catch (Exception ex) when (IsUncertain(ex)) { return CajaRecoveryOutcome.Uncertain; }
        }
        finally { _gate.Release(); }
    }

    public async Task<CajaRecoveryOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = store.Read();
            if (state?.Estado != "Pendiente" || state.Operacion is not Guid id || state.Propietario is not int owner ||
                currentActor() != owner || _observedPrepared != id) return CajaRecoveryOutcome.Uncertain;
            // Consume the one fresh preparation response before sending. Recovery cannot rearm execution.
            _observedPrepared = null;
            try { return RecordOutcome(id, owner, await transport.ExecuteAsync(id, owner, cancellationToken)); }
            catch (Exception ex) when (IsUncertain(ex)) { return CajaRecoveryOutcome.Uncertain; }
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = store.Read();
            if (state?.Estado != "TerminalPorConciliar" || state.Operacion is not Guid id ||
                state.Propietario is not int owner || state.Tipo is null || currentActor() != owner) return false;
            try
            {
                if (!await transport.ReconcileAsync(id, owner, state.Tipo, cancellationToken) || currentActor() != owner) return false;
                return store.CompleteReconciliation(id, owner);
            }
            catch (Exception ex) when (IsUncertain(ex))
            {
                return false;
            }
        }
        finally { _gate.Release(); }
    }

    private static bool IsUncertain(Exception ex) => ex is HttpRequestException or IOException or OperationCanceledException or
        System.Text.Json.JsonException or SessionExpiredException or SessionPausedException or SecurityConnectionException;

    private CajaRecoveryOutcome RecordOutcome(Guid operation, int owner, CajaRecoveryOutcome outcome, bool allowExecution = false)
    {
        _observedPrepared = null;
        if (currentActor() != owner) return CajaRecoveryOutcome.Uncertain;
        var state = store.Read();
        if (state?.Operacion != operation || state.Propietario != owner) return CajaRecoveryOutcome.Uncertain;
        if (allowExecution && outcome == CajaRecoveryOutcome.Prepared && state.Estado == "Pendiente") _observedPrepared = operation;
        if (outcome is not (CajaRecoveryOutcome.Completed or CajaRecoveryOutcome.Rejected or CajaRecoveryOutcome.Cancelled)) return outcome;
        if (state.Estado == "TerminalPorConciliar") return outcome;
        return store.MarkTerminal(operation, owner) ? outcome : CajaRecoveryOutcome.Uncertain;
    }
}
