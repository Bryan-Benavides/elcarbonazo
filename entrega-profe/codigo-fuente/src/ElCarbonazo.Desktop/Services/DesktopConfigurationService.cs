using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public sealed class DesktopConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<string, string?> _getEnvironmentVariable;

    public DesktopConfigurationService(string? configurationPath = null, Func<string, string?>? getEnvironmentVariable = null)
    {
        ConfigurationPath = configurationPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ElCarbonazo", "desktop.json");
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string ConfigurationPath { get; }

    public DesktopConfiguration Load()
    {
        var environment = _getEnvironmentVariable("ELCARBONAZO_ENVIRONMENT");
        var isDevelopment = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
        var overrideUrl = isDevelopment
            ? _getEnvironmentVariable("ELCARBONAZO_API_BASE_URL")
            : null;

        DesktopConfigurationFile? file = null;
        if (File.Exists(ConfigurationPath))
        {
            try
            {
                file = JsonSerializer.Deserialize<DesktopConfigurationFile>(
                    File.ReadAllText(ConfigurationPath),
                    JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new DesktopConfigurationException("El archivo de configuración contiene JSON inválido.", exception);
            }
            catch (IOException exception)
            {
                throw new DesktopConfigurationException("No fue posible leer el archivo de configuración.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new DesktopConfigurationException("La aplicación no tiene permiso de lectura sobre la configuración.", exception);
            }
        }
        else if (overrideUrl is null)
        {
            throw new DesktopConfigurationException("No se encontró la configuración instalada.");
        }

        var urlValue = overrideUrl ?? file?.ApiBaseUrl;
        if (!Uri.TryCreate(urlValue, UriKind.Absolute, out var apiBaseUrl) ||
            (apiBaseUrl.Scheme != Uri.UriSchemeHttps && !(isDevelopment && apiBaseUrl.Scheme == Uri.UriSchemeHttp)) ||
            !string.IsNullOrEmpty(apiBaseUrl.UserInfo) ||
            !string.IsNullOrEmpty(apiBaseUrl.Query) ||
            !string.IsNullOrEmpty(apiBaseUrl.Fragment))
        {
            throw new DesktopConfigurationException(
                "ApiBaseUrl debe ser una URL absoluta HTTPS sin credenciales, consulta ni fragmento.");
        }

        if (!Guid.TryParse(file?.IdInstalacion, out var installationId) || installationId == Guid.Empty)
        {
            throw new DesktopConfigurationException("idInstalacion debe contener un UUID válido generado por el instalador.");
        }

        var normalizedUrl = apiBaseUrl.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? apiBaseUrl
            : new Uri(apiBaseUrl.AbsoluteUri + '/', UriKind.Absolute);
        return new DesktopConfiguration(normalizedUrl, installationId);
    }

    private sealed record DesktopConfigurationFile(string? ApiBaseUrl, string? IdInstalacion);
}

public sealed class DesktopConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
