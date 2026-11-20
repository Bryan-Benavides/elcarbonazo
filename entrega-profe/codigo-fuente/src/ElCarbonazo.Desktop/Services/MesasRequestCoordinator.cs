namespace ElCarbonazo.Desktop.Services;

/// <summary>Coordina lecturas y mutaciones locales sin interpretar las versiones de la API.</summary>
public sealed class MesasRequestCoordinator
{
    private readonly object _gate = new();
    private long _generation;
    private long? _read;
    private bool _mutation;
    private long _mutationGeneration;
    private bool _online;
    private bool _sessionActive;
    private bool _uncertain;
    private bool _disposed;
    private TaskCompletionSource? _readFinished;

    public bool CanMutate
    {
        get { lock (_gate) return !_disposed && _sessionActive && _online && !_mutation && !_uncertain; }
    }

    public bool CanAcknowledgeUncertainty
    {
        get { lock (_gate) return !_disposed && _sessionActive && _online && !_mutation && _uncertain; }
    }

    public void SetSessionActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _sessionActive == active) return;
            _sessionActive = active;
            _online = false;
            _generation++;
        }
    }

    public long? TryBeginRead()
    {
        lock (_gate)
        {
            if (_disposed || !_sessionActive || _mutation || _read.HasValue) return null;
            _read = ++_generation;
            _readFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _read;
        }
    }

    public bool CompleteRead(long ticket, bool success, Action? apply = null)
    {
        lock (_gate)
        {
            if (_read != ticket) return false;
            _read = null;
            _readFinished?.TrySetResult();
            _readFinished = null;
            if (_mutation && !success) _online = false;
            if (_disposed || !_sessionActive || _mutation || ticket != _generation) return false;
            _online = success;
            if (success) apply?.Invoke();
            return true;
        }
    }

    public async Task<bool> TryBeginMutationAsync(CancellationToken cancellationToken = default)
    {
        Task? pendingRead;
        lock (_gate)
        {
            if (!CanMutate) return false;
            _mutation = true;
            _generation++;
            _mutationGeneration = _generation;
            pendingRead = _readFinished?.Task;
        }
        try
        {
            if (pendingRead is not null) await pendingRead.WaitAsync(cancellationToken);
        }
        catch
        {
            lock (_gate) { _mutation = false; _generation++; }
            throw;
        }
        lock (_gate)
        {
            if (!_disposed && _sessionActive && _online && _generation == _mutationGeneration) return true;
            _mutation = false;
            return false;
        }
    }

    public void CompleteMutation(bool requiresReconciliation, bool uncertain = false, Action? apply = null)
    {
        lock (_gate)
        {
            if (!_mutation) throw new InvalidOperationException("No hay una mutación activa.");
            if (!_disposed && _sessionActive && !uncertain && _generation == _mutationGeneration) apply?.Invoke();
            _mutation = false;
            _uncertain |= uncertain;
            if (requiresReconciliation || uncertain) _online = false;
            _generation++;
        }
    }

    public bool AcknowledgeUncertainty()
    {
        lock (_gate)
        {
            if (_disposed || !_sessionActive || !_online || _mutation || !_uncertain) return false;
            _uncertain = false;
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _disposed = true;
            _sessionActive = false;
            _online = false;
            _generation++;
        }
    }
}
