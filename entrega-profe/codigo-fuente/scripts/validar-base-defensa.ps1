param(
    [Parameter(Mandatory = $true)]
    [string] $BaseEsperada,
    [string] $ArchivoConfiguracion
)

$ErrorActionPreference = 'Stop'

# Solo lectura. No aplica migraciones, no aprovisiona nada y no imprime credenciales.
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArchivoConfiguracion)) {
    $ArchivoConfiguracion = Join-Path $root 'src\ElCarbonazo.Api\appsettings.Development.json'
}

function Fail([string] $mensaje) {
    Write-Output "[FALLA] $mensaje"
    exit 1
}
function Ok([string] $mensaje) { Write-Output "[OK]    $mensaje" }
$avisos = 0
function Warn([string] $mensaje) { Write-Output "[AVISO] $mensaje"; $script:avisos++ }

function Get-ConnectionString {
    $fromEnv = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Process')
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        Write-Output '[INFO]  Conexion tomada de ConnectionStrings__DefaultConnection del proceso.'
        return $fromEnv
    }
    if (-not (Test-Path -LiteralPath $ArchivoConfiguracion)) {
        Fail "No hay ConnectionStrings__DefaultConnection ni $ArchivoConfiguracion."
    }
    $json = Get-Content -Raw -LiteralPath $ArchivoConfiguracion | ConvertFrom-Json
    $value = $json.ConnectionStrings.DefaultConnection
    if ([string]::IsNullOrWhiteSpace($value)) {
        Fail "El archivo de configuracion no define ConnectionStrings:DefaultConnection."
    }
    Write-Output "[INFO]  Conexion tomada de $ArchivoConfiguracion."
    return $value
}

function Get-Part([string] $connectionString, [string[]] $nombres) {
    foreach ($nombre in $nombres) {
        if ($connectionString -match ('(?i)(^|;)\s*' + [regex]::Escape($nombre) + '\s*=\s*([^;]+)')) {
            return $matches[2].Trim()
        }
    }
    return $null
}

function Find-Psql {
    $command = Get-Command psql -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = Get-ChildItem 'C:\Program Files\PostgreSQL' -Recurse -Filter 'psql.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch 'pgAdmin' } |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    Fail 'No se encontro psql.exe. Instale el cliente de PostgreSQL o agreguelo al PATH.'
}

$connectionString = Get-ConnectionString
$host_ = Get-Part $connectionString @('Host', 'Server')
if ([string]::IsNullOrWhiteSpace($host_)) { $host_ = 'localhost' }
$port = Get-Part $connectionString @('Port')
if ([string]::IsNullOrWhiteSpace($port)) { $port = '5432' }
$user = Get-Part $connectionString @('Username', 'User ID', 'User')
$database = Get-Part $connectionString @('Database', 'Initial Catalog')

if ([string]::IsNullOrWhiteSpace($database)) { Fail 'No se pudo determinar el nombre de la base configurada.' }
if ($database -ne $BaseEsperada) {
    Fail "La base configurada es '$database' y se esperaba '$BaseEsperada'. No se consulto nada."
}
if ($host_ -notin @('localhost', '127.0.0.1', '::1')) {
    Fail "Host '$host_' no es local. Este script solo valida bases locales."
}

Write-Output "BASE VALIDADA: $BaseEsperada en ${host_}:$port"
$psql = Find-Psql

$sqlFile = Join-Path ([System.IO.Path]::GetTempPath()) ("validar-base-" + [guid]::NewGuid().ToString('N') + '.sql')
$sql = @'
select 'migraciones', count(*)::text from public."__EFMigrationsHistory";
select 'ultima_migracion', coalesce(max("MigrationId"), '') from public."__EFMigrationsHistory";
select 'migracion_006', count(*)::text from public."__EFMigrationsHistory" where "MigrationId" like '%AddAuditoriaCancelacionesSeguridad';
select 'migracion_reservas', count(*)::text from public."__EFMigrationsHistory" where "MigrationId" like '%AddReservaciones';
select 'productos', count(*)::text from producto;
select 'ingredientes_activos', count(*)::text from ingrediente where activo;
select 'ingredientes_sin_stock', count(*)::text from ingrediente where activo and stock_actual <= 0;
select 'ingredientes_bajo_minimo', count(*)::text from ingrediente where activo and stock_actual < stock_minimo;
select 'recetas_activas', count(*)::text from receta where activa;
select 'recetas_sin_lineas', count(*)::text from receta r where r.activa and not exists (select 1 from detalle_receta d where d.id_receta = r.id_receta);
select 'lineas_receta', count(*)::text from detalle_receta;
select 'jefas_activas', count(*)::text from usuario where rol = 'Jefa' and activo;
select 'usuarios_activos', count(*)::text from usuario where activo;
select 'hmac_provisionado', coalesce((select provisionado::text from estado_hmac_autorizacion where id = 1), 'fila_ausente');
select 'mesas', count(*)::text from mesa;
select 'mesas_no_libres', count(*)::text from mesa where estado <> 'Libre';
select 'turnos_abiertos', count(*)::text from turno_caja where estado = 'Abierto';
select 'pedidos_abiertos', count(*)::text from pedido where estado = 'Abierto';
select 'pedidos_abiertos_sin_lineas', count(*)::text from pedido p where p.estado = 'Abierto' and not exists (select 1 from comanda c join detalle_pedido d on d.id_comanda = c.id_comanda where c.id_pedido = p.id_pedido);
select 'operaciones_caja_no_terminales', count(*)::text from operacion_caja where estado not in ('Completada', 'Cancelada');
select 'anulaciones', count(*)::text from operacion_anulacion;
select 'reservaciones', count(*)::text from reservacion;
'@
$env:PGPASSWORD = Get-Part $connectionString @('Password', 'Pwd')
try {
    Set-Content -LiteralPath $sqlFile -Value $sql -Encoding UTF8
    $raw = & $psql -h $host_ -p $port -U $user -d $database -t -A -F '|' -v ON_ERROR_STOP=1 -f $sqlFile 2>&1
    if ($LASTEXITCODE -ne 0) {
        $raw | ForEach-Object { Write-Output $_ }
        Fail 'La consulta de solo lectura fallo. Revise conexion, permisos y que la base tenga el esquema aplicado.'
    }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $sqlFile -ErrorAction SilentlyContinue
}

$values = @{}
foreach ($line in $raw) {
    if ($line -isnot [string]) { continue }
    $parts = $line.Split('|')
    if ($parts.Count -ge 2) { $values[$parts[0]] = $parts[1] }
}

function Get-Number([string] $key) {
    if (-not $values.ContainsKey($key)) { return -1 }
    $parsed = 0
    if ([int]::TryParse($values[$key], [ref]$parsed)) { return $parsed }
    return -1
}

$failures = 0
function Check([bool] $condition, [string] $message) {
    if ($condition) { Ok $message } else { Write-Output "[FALLA] $message"; $script:failures++ }
}

Check ((Get-Number 'migraciones') -gt 0) "Migraciones EF aplicadas: $(Get-Number 'migraciones')"
Check ((Get-Number 'migracion_006') -gt 0) 'Migracion 006 de auditoria/cancelaciones aplicada'
Check ((Get-Number 'migracion_reservas') -gt 0) "Migracion de reservaciones aplicada (ultima: $($values['ultima_migracion']))"
Check ((Get-Number 'jefas_activas') -eq 1) "Exactamente una Jefa activa: $(Get-Number 'jefas_activas')"
Check ((Get-Number 'productos') -gt 0) "Productos: $(Get-Number 'productos')"
Check ((Get-Number 'ingredientes_activos') -gt 0) "Ingredientes activos: $(Get-Number 'ingredientes_activos')"
Check ((Get-Number 'recetas_activas') -gt 0) "Recetas activas: $(Get-Number 'recetas_activas')"
Check ((Get-Number 'recetas_sin_lineas') -eq 0) "Recetas activas sin lineas: $(Get-Number 'recetas_sin_lineas')"
Check ((Get-Number 'lineas_receta') -gt 0) "Lineas de receta: $(Get-Number 'lineas_receta')"

if ((Get-Number 'ingredientes_sin_stock') -gt 0) {
    Warn "Ingredientes activos sin stock: $(Get-Number 'ingredientes_sin_stock'). El envio de comandas los rechazara."
}
if ((Get-Number 'ingredientes_bajo_minimo') -gt 0) {
    Warn "Ingredientes bajo el minimo: $(Get-Number 'ingredientes_bajo_minimo')."
}
if ((Get-Number 'operaciones_caja_no_terminales') -gt 0) {
    Warn "Operaciones de caja no terminales: $(Get-Number 'operaciones_caja_no_terminales'). No las reintente sin conciliar."
}
if ((Get-Number 'anulaciones') -gt 0) {
    Write-Output "[INFO]  Anulaciones registradas: $(Get-Number 'anulaciones')."
}
if ((Get-Number 'reservaciones') -eq 0) {
    Warn 'No hay reservaciones cargadas; la pantalla de reservas estara vacia.'
}

if ($values['hmac_provisionado'] -ne 'true') {
    Warn "HMAC de autorizacion no provisionado ($($values['hmac_provisionado'])). La anulacion respondera AUTORIZACION_SUPERIOR_NO_DISPONIBLE."
    $hmacKey = [Environment]::GetEnvironmentVariable('AutorizacionSuperior__ClaveHmac', 'Process')
    if ([string]::IsNullOrWhiteSpace($hmacKey)) {
        Warn 'AutorizacionSuperior__ClaveHmac no esta definida en este proceso.'
    }
}
else {
    Ok 'HMAC de autorizacion provisionado'
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('AutorizacionSuperior__ClaveHmac', 'Process'))) {
        Warn 'HMAC provisionado en la base, pero la clave no esta definida en este proceso; la API debe iniciarse con ella.'
    }
}

if ((Get-Number 'turnos_abiertos') -gt 0) {
    Warn "Hay $(Get-Number 'turnos_abiertos') turno(s) de caja abierto(s): no se podra abrir otro hasta cerrarlo."
}
if ((Get-Number 'mesas_no_libres') -gt 0) {
    Write-Output "[INFO]  Mesas no libres: $(Get-Number 'mesas_no_libres') de $(Get-Number 'mesas')."
}
if ((Get-Number 'pedidos_abiertos_sin_lineas') -gt 0) {
    Warn "Pedidos abiertos sin lineas: $(Get-Number 'pedidos_abiertos_sin_lineas'). No se pueden cobrar y bloquean su mesa."
}

Write-Output ''
if ($failures -gt 0) {
    Write-Output "RESULTADO: la base NO esta lista ($failures fallas). No se modifico nada."
    exit 1
}
if ($avisos -gt 0) {
    Write-Output "RESULTADO: base preparada con $avisos aviso(s). Revise la lista antes de presentar; no se modifico nada."
    exit 0
}
Write-Output 'RESULTADO: base preparada para la demostracion local, sin avisos. No se modifico nada.'
exit 0
