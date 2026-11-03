using System.Text;
using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

public sealed class DesktopJsonLogger
{
    private const long MaximumDailyBytes = 20L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _directory;

    public DesktopJsonLogger()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ElCarbonazo",
            "logs");
    }

    public void Information(string eventName, string message, string? traceId = null) =>
        Write("Information", eventName, message, traceId);

    public void Warning(string eventName, string message, string? traceId = null) =>
        Write("Warning", eventName, message, traceId);

    public void Error(string eventName, string message, string? traceId = null) =>
        Write("Error", eventName, message, traceId);

    private void Write(string level, string eventName, string message, string? traceId)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var now = DateTimeOffset.UtcNow;
                var path = Path.Combine(_directory, $"desktop-{now:yyyyMMdd}.jsonl");
                var entry = JsonSerializer.Serialize(new
                {
                    timestamp = now,
                    level,
                    eventName,
                    message,
                    traceId
                }) + Environment.NewLine;
                var bytes = Encoding.UTF8.GetByteCount(entry);
                var currentLength = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (bytes <= MaximumDailyBytes && currentLength + bytes <= MaximumDailyBytes)
                {
                    File.AppendAllText(path, entry, Encoding.UTF8);
                }

                foreach (var oldPath in Directory.EnumerateFiles(_directory, "desktop-????????.jsonl"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(oldPath);
                    if (DateTime.TryParseExact(
                            fileName.AsSpan("desktop-".Length),
                            "yyyyMMdd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal,
                            out var fileDate) &&
                        fileDate.Date < DateTime.UtcNow.Date.AddDays(-6))
                    {
                        File.Delete(oldPath);
                    }
                }
            }
            catch
            {
                // El diagnóstico nunca debe impedir el uso o cierre de la aplicación.
            }
        }
    }
}
