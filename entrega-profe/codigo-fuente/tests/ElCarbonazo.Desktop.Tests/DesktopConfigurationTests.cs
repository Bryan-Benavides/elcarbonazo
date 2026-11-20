using System.Text.Json;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class DesktopConfigurationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"elcarbonazo-config-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void MissingConfigurationIsRejected()
    {
        var service = new DesktopConfigurationService(_path, _ => null);
        Assert.Throws<DesktopConfigurationException>(() => service.Load());
    }

    [Theory]
    [InlineData("http://localhost:5261", "00000000-0000-0000-0000-000000000001")]
    [InlineData("https://example.local", "invalid")]
    [InlineData("https://user:password@example.local", "00000000-0000-0000-0000-000000000001")]
    public void InvalidProductionSettingsAreRejected(string url, string id)
    {
        Write(url, id);
        var service = new DesktopConfigurationService(_path, _ => null);
        Assert.Throws<DesktopConfigurationException>(() => service.Load());
    }

    [Fact]
    public void EnvironmentOverrideIsAllowedOnlyInDevelopment()
    {
        var id = Guid.NewGuid();
        Write("https://example.local", id.ToString());
        var production = new DesktopConfigurationService(_path, key => key == "ELCARBONAZO_API_BASE_URL" ? "http://localhost:5261" : null);
        Assert.Equal("https://example.local/", production.Load().ApiBaseUrl.ToString());
        var development = new DesktopConfigurationService(_path, key => key switch
        {
            "ELCARBONAZO_ENVIRONMENT" => "Development",
            "ELCARBONAZO_API_BASE_URL" => "http://localhost:5261",
            _ => null
        });
        var config = development.Load();
        Assert.Equal("http://localhost:5261/", config.ApiBaseUrl.ToString());
        Assert.Equal(id, config.IdInstalacion);
    }

    private void Write(string url, string id) => File.WriteAllText(_path, JsonSerializer.Serialize(new { apiBaseUrl = url, idInstalacion = id }));
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
