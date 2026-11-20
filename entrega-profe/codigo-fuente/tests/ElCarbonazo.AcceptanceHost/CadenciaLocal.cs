using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;

internal static class CadenciaLocal
{
    public static async Task RunAsync(HttpClient client, CancellationToken token)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("api/auth/login", new { nombre = "cocina", contrasena = "kitchen password" }, token);
        login.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        var service = new InstrumentedMesas(new MesasApiService(new PollingSession(client)));
        using var vm = new MesasViewModel("cocina", "Personal de Cocina", new PollingSession(client), service,
            action => { action(); return Task.CompletedTask; });
        var samples = new List<(long Start, double Duration)>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 6; i++) samples.Add(await service.Samples.Reader.ReadAsync(deadline.Token));
        var intervals = samples.Zip(samples.Skip(1), (a, b) => Stopwatch.GetElapsedTime(a.Start, b.Start).TotalMilliseconds).ToArray();
        Console.WriteLine("CADENCIA LOCAL: 6 GET satisfactorios mediante MesasViewModel y MesasApiService contra API temporal HTTP en loopback.");
        Console.WriteLine($"Intervalos entre inicios (ms): {string.Join(", ", intervals.Select(x => x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}");
        Console.WriteLine($"Duraciones GET/deserialización (ms): {string.Join(", ", samples.Select(x => x.Duration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}");
        Console.WriteLine($"GET simultáneos máximos: {service.MaxConcurrent}; despacho inline, sin ventana ni render; sesión simulada.");
    }

    private sealed class InstrumentedMesas(IMesasApiService inner) : IMesasApiService
    {
        private int _active;
        private int _maxConcurrent;
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public Channel<(long Start, double Duration)> Samples { get; } = Channel.CreateUnbounded<(long, double)>();
        public async Task<IReadOnlyList<MesaDto>> GetAsync(CancellationToken cancellationToken = default)
        {
            var start = Stopwatch.GetTimestamp();
            var active = Interlocked.Increment(ref _active);
            int prior;
            do { prior = _maxConcurrent; if (prior >= active) break; } while (Interlocked.CompareExchange(ref _maxConcurrent, active, prior) != prior);
            try
            {
                var result = await inner.GetAsync(cancellationToken);
                Samples.Writer.TryWrite((start, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
                return result;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public Task<MesaDto> UpdateEstadoAsync(int id, string estado, uint version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PollingSession(HttpClient client) : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> factory, CancellationToken cancellationToken = default) => SendAsync(factory, cancellationToken);
        private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken token)
        {
            using var request = factory();
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
        }
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
