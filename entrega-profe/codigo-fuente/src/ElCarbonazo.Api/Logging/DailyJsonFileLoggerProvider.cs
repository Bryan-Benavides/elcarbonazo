using System.Text.Json;

namespace ElCarbonazo.Api.Logging;

public sealed class DailyJsonFileLoggerProvider(string directoryPath) : ILoggerProvider
{
    private const long MaximumFileBytes = 100 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ILogger CreateLogger(string categoryName) => new DailyJsonFileLogger(this, categoryName);

    internal void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
                var now = DateTimeOffset.UtcNow;
                var filePath = Path.Combine(directoryPath, $"api-{now:yyyyMMdd}.jsonl");
                var file = new FileInfo(filePath);
                if (file.Exists && file.Length >= MaximumFileBytes)
                {
                    return;
                }

                var entry = JsonSerializer.Serialize(new
                {
                    timestamp = now,
                    level = level.ToString(),
                    category,
                    eventId = eventId.Id,
                    message,
                    exception = exception?.ToString()
                }, _jsonOptions);
                var entryBytes = System.Text.Encoding.UTF8.GetByteCount(entry);
                if (entryBytes + System.Text.Encoding.UTF8.GetByteCount(Environment.NewLine) > MaximumFileBytes)
                {
                    return;
                }

                if (file.Exists && file.Length + entryBytes + System.Text.Encoding.UTF8.GetByteCount(Environment.NewLine) > MaximumFileBytes)
                {
                    return;
                }

                File.AppendAllText(filePath, entry + Environment.NewLine, System.Text.Encoding.UTF8);
                RemoveOldFiles(now.UtcDateTime.Date);
            }
            catch
            {
                // Logging must not crash application requests; the JSON console remains active.
            }
        }
    }

    private void RemoveOldFiles(DateTime currentDate)
    {
        foreach (var path in Directory.EnumerateFiles(directoryPath, "api-????????.jsonl"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (DateTime.TryParseExact(
                    fileName.AsSpan(4),
                    "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var date) &&
                date.Date < currentDate.AddDays(-29))
            {
                File.Delete(path);
            }
        }
    }

    public void Dispose() { }

    private sealed class DailyJsonFileLogger(DailyJsonFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
