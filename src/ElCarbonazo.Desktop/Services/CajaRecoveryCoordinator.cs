namespace ElCarbonazo.Desktop.Services;

public enum CajaRecoveryOutcome { Prepared, NotFound, Completed, Rejected, Cancelled, Uncertain }

// Transport must use the specified actor's current authenticated session and must never retry writes.
public interface ICajaRecoveryTransport
{
    Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken);
    Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken);
    Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken);
    Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken);
    Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken);
}

// The application wires this coordinator to REST; C3/C4 must supply typed requests and business reconciliation.
public sealed class CajaRecoveryCoordinator(CajaRecoveryStore store, ICajaRecoveryTransport transport, Func<int?> currentActor, Func<bool>? isJefa = null, Func<long>? sessionGeneration = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _observedPrepared;
    private long _preparedGeneration;

    public async Task<CajaRecoveryOutcome> BeginAsync(Guid operation, int actor, string type, CancellationToken cancellationToken = default, object? arguments = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionGeneration?.Invoke() ?? 0;
            if (currentActor() != actor || !store.Begin(operation, actor, type)) return CajaRecoveryOutcome.Uncertain;
            if (currentActor() != actor || !SameGeneration(generation)) return CajaRecoveryOutcome.Uncertain;
            try
            {
                var outcome = await transport.PrepareAsync(operation, actor, type, arguments, cancellationToken);
                if (!SameGeneration(generation)) return CajaRecoveryOutcome.Uncertain;
                var result = RecordOutcome(operation, actor, outcome, allowExecution: true);
                if (_observedPrepared == operation) _preparedGeneration = generation;
                return result;
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
            var generation = sessionGeneration?.Invoke() ?? 0;
            var state = store.Read();
            if (state?.Operacion is not Guid id || state.Propietario is not int owner || currentActor() is not int actor || !PuedeSupervisar(owner, actor))
                return CajaRecoveryOutcome.Uncertain;
            _observedPrepared = null;
            try
            {
                var outcome = await transport.QueryAsync(id, actor, cancellationToken);
                if (!SameGeneration(generation)) return CajaRecoveryOutcome.Uncertain;
                return RecordOutcome(id, owner, outcome, actor: actor);
            }
            catch (Exception ex) when (IsUncertain(ex))
            {
                return CajaRecoveryOutcome.Uncertain;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<CajaRecoveryOutcome> CancelAsync(CancellationToken cancellationToken = default, bool confirmaIntervencion = false)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionGeneration?.Invoke() ?? 0;
            var state = store.Read();
            if (state?.Estado != "Pendiente" || state.Operacion is not Guid id || state.Propietario is not int owner ||
                currentActor() is not int actor || !PuedeSupervisar(owner, actor) || (actor != owner && !confirmaIntervencion))
                return CajaRecoveryOutcome.Uncertain;
            _observedPrepared = null;
            try
            {
                var outcome = await transport.CancelAsync(id, actor, actor != owner && confirmaIntervencion, cancellationToken);
                return SameGeneration(generation) ? RecordOutcome(id, owner, outcome, actor: actor) : CajaRecoveryOutcome.Uncertain;
            }
            catch (Exception ex) when (IsUncertain(ex)) { return CajaRecoveryOutcome.Uncertain; }
        }
        finally { _gate.Release(); }
    }

    public async Task<CajaRecoveryOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionGeneration?.Invoke() ?? 0;
            var state = store.Read();
            if (state?.Estado != "Pendiente" || state.Operacion is not Guid id || state.Propietario is not int owner ||
                currentActor() != owner || _observedPrepared != id || _preparedGeneration != generation) return CajaRecoveryOutcome.Uncertain;
            // Consume the one fresh preparation response before sending. Recovery cannot rearm execution.
            _observedPrepared = null;
            try
            {
                var outcome = await transport.ExecuteAsync(id, owner, cancellationToken);
                return SameGeneration(generation) ? RecordOutcome(id, owner, outcome) : CajaRecoveryOutcome.Uncertain;
            }
            catch (Exception ex) when (IsUncertain(ex)) { return CajaRecoveryOutcome.Uncertain; }
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionGeneration?.Invoke() ?? 0;
            var state = store.Read();
            if (state?.Estado != "TerminalPorConciliar" || state.Operacion is not Guid id ||
                state.Propietario is not int owner || state.Tipo is null || currentActor() is not int actor || !PuedeSupervisar(owner, actor)) return false;
            try
            {
                if (!await transport.ReconcileAsync(id, actor, state.Tipo, cancellationToken) || !SameGeneration(generation) || !PuedeSupervisar(owner, actor)) return false;
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

    private bool PuedeSupervisar(int owner, int actor) => currentActor() == actor && (owner == actor || isJefa?.Invoke() == true);
    private bool SameGeneration(long generation) => (sessionGeneration?.Invoke() ?? 0) == generation;

    private CajaRecoveryOutcome RecordOutcome(Guid operation, int owner, CajaRecoveryOutcome outcome, bool allowExecution = false, int? actor = null)
    {
        _observedPrepared = null;
        if (!PuedeSupervisar(owner, actor ?? owner)) return CajaRecoveryOutcome.Uncertain;
        var state = store.Read();
        if (state?.Operacion != operation || state.Propietario != owner) return CajaRecoveryOutcome.Uncertain;
        if (allowExecution && outcome == CajaRecoveryOutcome.Prepared && state.Estado == "Pendiente") _observedPrepared = operation;
        if (outcome is not (CajaRecoveryOutcome.Completed or CajaRecoveryOutcome.Rejected or CajaRecoveryOutcome.Cancelled)) return outcome;
        if (state.Estado == "TerminalPorConciliar") return outcome;
        return store.MarkTerminal(operation, owner) ? outcome : CajaRecoveryOutcome.Uncertain;
    }
}
