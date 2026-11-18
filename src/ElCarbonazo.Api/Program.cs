using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Http;
using ElCarbonazo.Api.Logging;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddProvider(new DailyJsonFileLoggerProvider(
    builder.Configuration["Logging:FileDirectory"] ??
    Path.Combine(
        builder.Environment.IsDevelopment()
            ? builder.Environment.ContentRootPath
            : Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ElCarbonazo",
        "api-logs")));
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Npgsql", LogLevel.Warning);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = actionContext =>
        {
            var errors = actionContext.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                    entry => entry.Value!.Errors.Select(error =>
                        string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "El valor enviado no es válido."
                            : error.ErrorMessage).ToArray());

            var problem = new ValidationProblemDetails(errors)
            {
                Type = "https://elcarbonazo.local/problems/validation_error",
                Title = "La solicitud contiene campos inválidos.",
                Status = StatusCodes.Status400BadRequest,
                Instance = actionContext.HttpContext.Request.Path
            };
            problem.Extensions["code"] = "VALIDATION_ERROR";
            problem.Extensions["traceId"] = actionContext.HttpContext.TraceIdentifier;

            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "code",
            context.ProblemDetails.Status == StatusCodes.Status500InternalServerError
                ? "API_ERROR_INTERNO"
                : "HTTP_ERROR");
        context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
    };
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .PostConfigure(settings =>
    {
        if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(settings.Key))
        {
            settings.Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
        }
    })
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Key),
        "Jwt:Key debe configurarse mediante una fuente segura.")
    .Validate(settings => HasValidJwtKey(settings.Key),
        "Jwt:Key debe ser Base64 válido y contener al menos 32 bytes aleatorios al decodificar.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer),
        "Jwt:Issuer es obligatorio.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience),
        "Jwt:Audience es obligatorio.")
    .Validate(settings => settings.ExpirationMinutes == 5,
        "Jwt:ExpirationMinutes debe ser exactamente 5 minutos.")
    .ValidateOnStart();

builder.Services
    .AddOptions<PasswordHashingOptions>()
    .Bind(builder.Configuration.GetSection(PasswordHashingOptions.SectionName))
    .Validate(options => options.WorkFactor is >= 10 and <= 15,
        "PasswordHashing:WorkFactor debe estar entre 10 y 15.")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    options.OnRejected = async (rejection, cancellationToken) =>
    {
        var retryAfter = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))
            : 60;
        rejection.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        var services = rejection.HttpContext.RequestServices;
        var ipAddress = rejection.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var auditGate = services.GetRequiredService<LoginRateLimitAuditGate>();
        if (auditGate.TryBeginWindow(ipAddress))
        {
            try
            {
                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditRecorder>();
                var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                audit.Record(
                    "LoginRateLimitActivado",
                    "Rechazado",
                    entityType: "ip",
                    entityId: ipAddress,
                    details: new Dictionary<string, object?>
                    {
                        ["ventanaUnixMinuto"] = time.GetUtcNow().ToUnixTimeSeconds() / 60
                    });
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("LoginRateLimitAudit")
                    .LogError(exception, "Could not persist the first login rate-limit audit event");
            }
        }
        await ApiProblemDetails.WriteAsync(
            rejection.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "AUTH_DEMASIADOS_INTENTOS",
            "Demasiados intentos",
            "Se alcanzó el límite temporal de solicitudes de login.",
            cancellationToken);
    };
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginRateLimitAuditGate>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddHostedService<PasswordHasherWarmupService>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
builder.Services.AddScoped<DemoCatalogSeeder>();
builder.Services.AddScoped<CatalogoDefensaSeeder>();
builder.Services.AddScoped<IAuditRecorder, AuditRecorder>();
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMesasService, MesasService>();
builder.Services.AddScoped<IGruposMesaService, GruposMesaService>();
builder.Services.AddScoped<ReservacionesService>();
builder.Services.AddScoped<ReporteVentasService>();
builder.Services.AddScoped<IProductosService, ProductosService>();
builder.Services.AddScoped<ICuentasService, CuentasService>();
builder.Services.AddScoped<EnviosService>();
builder.Services.AddScoped<InventarioService>();
builder.Services.AddScoped<InventarioConsultasService>();
builder.Services.AddScoped<RecetasService>();
builder.Services.AddScoped<CocinaService>();
builder.Services.AddScoped<AnulacionAutorizacionService>();
builder.Services.AddScoped<AnulacionEjecucionService>();
builder.Services.AddScoped<AnulacionRecuperacionService>();
builder.Services.AddScoped<AnulacionesConsultasService>();
builder.Services.AddScoped<CajaOperacionesService>();
builder.Services.AddScoped<CajaCancelacionService>();
builder.Services.AddScoped<CajaCuentasService>();
builder.Services.AddScoped<CajaTurnosService>();
builder.Services.AddScoped<CajaComprobantesService>();
builder.Services.AddScoped<CajaAperturaExecutor>();
builder.Services.AddScoped<CajaCierreExecutor>();
builder.Services.AddScoped<CajaCobroExecutor>();
builder.Services.AddScoped<ICajaEjecutorDominio, CajaEjecutorDominio>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
    {
        var jwtSettings = jwtOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtSettings.Key)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = "rol"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (IsSessionExpired(context.AuthenticateFailure))
                {
                    await ApiProblemDetails.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status401Unauthorized,
                        "AUTH_SESION_EXPIRADA",
                        "Sesión expirada",
                        "La sesión ya no está activa.",
                        context.HttpContext.RequestAborted);
                    return;
                }

                var expired = IsExpiredToken(context.AuthenticateFailure);
                await ApiProblemDetails.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    expired ? "AUTH_ACCESS_TOKEN_EXPIRADO" : "AUTH_NO_AUTENTICADO",
                    expired ? "Access token expirado" : "Autenticación requerida",
                    expired
                        ? "El access token expiró."
                        : "Se requiere un access token válido.",
                    context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
            {
                await ApiProblemDetails.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "AUTH_PROHIBIDO",
                    "Acceso prohibido",
                    "El rol autenticado no tiene permiso para esta operación.",
                    context.HttpContext.RequestAborted);
            },
            OnTokenValidated = async context =>
            {
                var sessionClaim = context.Principal?.FindFirst("sid")?.Value;
                if (!Guid.TryParse(sessionClaim, out var sessionId))
                {
                    context.Fail(new SessionExpiredException());
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                var session = await db.SesionesUsuario
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.IdSesion == sessionId,
                        context.HttpContext.RequestAborted);
                var now = time.GetUtcNow();
                var subject = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                              context.Principal?.FindFirst("sub")?.Value;
                if (session is null ||
                    !int.TryParse(subject, out var userId) ||
                    session.IdUsuario != userId ||
                    session.FechaRevocacion is not null ||
                    now >= session.ExpiracionAbsoluta ||
                    now >= session.UltimaActividad.Add(SessionSecurityPolicy.InactivityLifetime))
                {
                    context.Fail(new SessionExpiredException());
                    return;
                }

                var role = context.Principal?.FindFirst("rol")?.Value;
                if (role is null || !await db.Usuarios.AsNoTracking().AnyAsync(
                        user => user.IdUsuario == userId && user.Activo && user.Rol == role,
                        context.HttpContext.RequestAborted))
                {
                    context.Fail(new SessionExpiredException());
                }
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();

var commandModes = new[] { "--bootstrap-admin", "--seed-demo-catalog", "--seed-catalogo-defensa", "--provisionar-hmac-autorizacion", "--recuperar-hmac-autorizacion" };
if (commandModes.Count(mode => args.Contains(mode, StringComparer.Ordinal)) > 1)
{
    Console.Error.WriteLine("Seleccione únicamente una acción administrativa.");
    return 1;
}

if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    return await AdminBootstrapCommand.RunAsync(
        scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>(),
        CancellationToken.None);
}

if (args.Contains("--seed-demo-catalog", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    return await DemoCatalogCommand.RunAsync(
        scope.ServiceProvider.GetRequiredService<DemoCatalogSeeder>(),
        CancellationToken.None);
}

if (args.Contains("--seed-catalogo-defensa", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment() ||
        builder.Configuration["CatalogoDefensa:ConfirmarBase"] != "elcarbonazo" ||
        string.IsNullOrWhiteSpace(builder.Configuration["CatalogoDefensa:Directorio"]))
    {
        Console.Error.WriteLine("La carga requiere Development, directorio explícito y confirmación exacta de la base.");
        return 1;
    }
    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var actors = await context.Usuarios.AsNoTracking().Where(x => x.Activo && x.Rol == "Jefa")
            .Select(x => x.IdUsuario).Take(2).ToListAsync();
        if (actors.Count != 1) throw new InvalidOperationException("Se requiere exactamente una Jefa activa.");
        await scope.ServiceProvider.GetRequiredService<CatalogoDefensaSeeder>().CargarAsync(
            builder.Configuration["CatalogoDefensa:Directorio"]!, actors[0], "elcarbonazo", CancellationToken.None);
        Console.WriteLine("Catálogo validado y cargado, o ya coincidía exactamente.");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.EntityFrameworkCore.DbUpdateException or Npgsql.PostgresException)
    {
        Console.Error.WriteLine("No se confirmó la carga; no repetir automáticamente. Revise el destino y el estado antes de continuar.");
        return 1;
    }
}

if (args.Contains("--provisionar-hmac-autorizacion", StringComparer.Ordinal) ||
    args.Contains("--recuperar-hmac-autorizacion", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    return await HmacProvisionCommand.RunAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<IAuditRecorder>(),
        builder.Configuration["AutorizacionSuperior:ClaveHmac"],
        scope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value.Key,
        Console.In, Console.Out, CancellationToken.None,
        args.Contains("--recuperar-hmac-autorizacion", StringComparer.Ordinal));
}

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>()
        .SeedDevelopmentAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.Use(async (httpContext, next) =>
{
    if (httpContext.Request.Headers.TryGetValue("X-Installation-Id", out var installationIds) &&
        (installationIds.Count != 1 || !Guid.TryParse(installationIds[0], out _)))
    {
        var problem = ApiProblemDetails.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            "VALIDATION_ERROR",
            "Identificador de instalación inválido",
            "X-Installation-Id debe contener un UUID válido.");
        problem.Extensions["errors"] = new Dictionary<string, string[]>
        {
            ["xInstallationId"] = ["El valor debe ser un UUID válido."]
        };
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problem,
            JsonSerializerOptions.Web,
            httpContext.RequestAborted);
        return;
    }

    await next();
});
app.UseStatusCodePages(async statusContext =>
{
    var httpContext = statusContext.HttpContext;
    var status = httpContext.Response.StatusCode;
    var (code, title, detail) = status switch
    {
        StatusCodes.Status404NotFound => ("HTTP_NO_ENCONTRADO", "Recurso no encontrado", "La ruta solicitada no existe."),
        StatusCodes.Status405MethodNotAllowed => ("HTTP_METODO_NO_PERMITIDO", "Método no permitido", "El método HTTP no está permitido para este recurso."),
        _ => ("HTTP_ERROR", "Solicitud no completada", "La solicitud no pudo completarse.")
    };
    await ApiProblemDetails.WriteAsync(httpContext, status, code, title, detail, httpContext.RequestAborted);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
return 0;

static bool IsExpiredToken(Exception? exception)
{
    while (exception is not null)
    {
        if (exception is SecurityTokenExpiredException)
        {
            return true;
        }
        exception = exception.InnerException;
    }

    return false;
}

static bool HasValidJwtKey(string key)
{
    try
    {
        return Convert.FromBase64String(key).Length >= 32;
    }
    catch (FormatException)
    {
        return false;
    }
}

static bool IsSessionExpired(Exception? exception)
{
    while (exception is not null)
    {
        if (exception is SessionExpiredException)
        {
            return true;
        }
        exception = exception.InnerException;
    }

    return false;
}

public partial class Program { }
