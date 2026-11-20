param([switch]$RecuperarDiarioDeEstaDemo)

$ErrorActionPreference = 'Stop'

# Usar solo con la base local de demostración ya preparada; no crea bases ni diarios.
$root = Split-Path -Parent $PSScriptRoot
$apiDirectory = Join-Path $root 'src\ElCarbonazo.Api'
$project = Join-Path $apiDirectory 'ElCarbonazo.Api.csproj'
$configPath = Join-Path $apiDirectory 'appsettings.Development.json'
$applicationDirectory = Join-Path $env:ProgramData 'ElCarbonazo'
$desktopPath = Join-Path $applicationDirectory 'desktop.json'
$journalPath = Join-Path $applicationDirectory 'recovery\caja-journal.json'
$lockPath = "$journalPath.lock"

if (Get-Process -Name 'ElCarbonazo.Desktop' -ErrorAction SilentlyContinue) {
    throw 'Cierre Desktop antes de iniciar la API de demostración y cambiar de base.'
}
if (!(Test-Path -LiteralPath $desktopPath) -or !(Test-Path -LiteralPath $journalPath) -or !(Test-Path -LiteralPath $lockPath)) {
    throw 'Falta la configuración o el diario de caja. No cree ni reemplace un diario para desbloquearlo.'
}

# El bloqueo evita leer el diario mientras otra instancia lo está modificando.
$gate = [System.IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None')
try {
    $desktop = Get-Content -Raw -LiteralPath $desktopPath | ConvertFrom-Json
    $journal = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json
    $desktopId = [guid]::Empty
    $journalId = [guid]::Empty
    $names = @($journal.PSObject.Properties.Name)
    $expectedNames = @('version', 'instalacion', 'estado', 'operacion', 'propietario', 'tipo')
    if ($names.Count -ne 6 -or @($expectedNames | Where-Object { $_ -cnotin $names }).Count -ne 0 -or
        ![guid]::TryParse($desktop.idInstalacion, [ref]$desktopId) -or $desktopId -eq [guid]::Empty -or
        ![guid]::TryParse($journal.instalacion, [ref]$journalId) -or $desktopId -ne $journalId -or
        $journal.version -ne 1) {
        throw 'El diario es inválido o no coincide con la instalación. No lo reemplace.'
    }
    if ($journal.estado -ceq 'SinPendientes') {
        if ($null -ne $journal.operacion -or $null -ne $journal.propietario -or $null -ne $journal.tipo) {
            throw 'El diario contiene datos contradictorios. No lo reemplace.'
        }
    }
    else {
        $operationId = [guid]::Empty
        if ($journal.estado -cnotin @('Pendiente', 'TerminalPorConciliar') -or
            ![guid]::TryParse($journal.operacion, [ref]$operationId) -or $operationId -eq [guid]::Empty -or
            $journal.propietario -isnot [int] -or $journal.propietario -le 0 -or
            $journal.tipo -cnotin @('AbrirTurno', 'CobrarCuenta', 'CerrarTurno')) {
            throw 'El diario contiene un estado inválido. No lo reemplace.'
        }
        if (!$RecuperarDiarioDeEstaDemo) {
            throw 'Hay una operación pendiente. Confirme primero que pertenece a esta base; para recuperarla use -RecuperarDiarioDeEstaDemo.'
        }
        Write-Warning 'Se iniciará la API para recuperar el diario existente. No se ejecutará ni borrará la operación automáticamente.'
    }
}
finally {
    $gate.Dispose()
}

$connection = $env:ConnectionStrings__DefaultConnection
if ([string]::IsNullOrWhiteSpace($connection)) {
    if (!(Test-Path -LiteralPath $configPath)) { throw 'Falta la conexión de Development.' }
    $connection = (Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json).ConnectionStrings.DefaultConnection
}
if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Falta la conexión de Development.' }

$builder = New-Object System.Data.Common.DbConnectionStringBuilder
$builder.set_ConnectionString($connection)
if (!$builder.ContainsKey('Database') -or $builder.ContainsKey('Initial Catalog') -or
    !$builder.ContainsKey('Host') -or $builder.ContainsKey('Server') -or
    [string]$builder['Host'] -notin @('localhost', '127.0.0.1') -or
    ($builder.ContainsKey('Port') -and [string]$builder['Port'] -ne '5432')) {
    throw 'La conexión no coincide con el PostgreSQL local de demostración; no se iniciará la API.'
}
$builder['Database'] = 'elcarbonazo_demo_local'
$previousConnection = $env:ConnectionStrings__DefaultConnection
try {
    $env:ConnectionStrings__DefaultConnection = $builder.get_ConnectionString()
    Write-Host 'Iniciando API HTTPS con la base local elcarbonazo_demo_local. Ctrl+C para detenerla.'
    & dotnet run --project $project --launch-profile https
    if ($LASTEXITCODE -ne 0) { throw 'La API terminó con error. Compruebe el puerto y la configuración local.' }
}
finally {
    $env:ConnectionStrings__DefaultConnection = $previousConnection
}
