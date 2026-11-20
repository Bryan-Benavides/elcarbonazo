namespace ElCarbonazo.Desktop.Services;

public interface IServerClock
{
    DateTimeOffset UtcNow { get; }
    void Synchronize(DateTimeOffset serverTime, DateTimeOffset sentAt, DateTimeOffset receivedAt);
    DateTimeOffset ToManaguaTime(DateTimeOffset utcInstant);
}

public sealed class ServerClock : IServerClock
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _managuaTimeZone;
    private readonly object _gate = new();
    private TimeSpan _offset;

    public ServerClock(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _managuaTimeZone = FindManaguaTimeZone();
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _timeProvider.GetUtcNow() + _offset;
            }
        }
    }

    public void Synchronize(
        DateTimeOffset serverTime,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt)
    {
        var midpoint = sentAt + TimeSpan.FromTicks((receivedAt - sentAt).Ticks / 2);
        lock (_gate)
        {
            _offset = serverTime - midpoint;
        }
    }

    public DateTimeOffset ToManaguaTime(DateTimeOffset utcInstant) =>
        TimeZoneInfo.ConvertTime(utcInstant, _managuaTimeZone);

    private static TimeZoneInfo FindManaguaTimeZone()
    {
        foreach (var id in new[] { "America/Managua", "Central America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        throw new InvalidOperationException("No se encontró la zona horaria de Managua en el sistema operativo.");
    }
}
