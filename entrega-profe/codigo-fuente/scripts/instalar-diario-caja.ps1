param(
    [Parameter(Mandatory = $true)]
    [string] $DesktopExe
)

$ErrorActionPreference = 'Stop'
$applicationDirectory = Join-Path $env:ProgramData 'ElCarbonazo'
$configuration = Join-Path $applicationDirectory 'desktop.json'
$recovery = Join-Path $applicationDirectory 'recovery'

if (-not (Test-Path -LiteralPath $DesktopExe -PathType Leaf)) {
    throw 'No se encontró el ejecutable Desktop indicado.'
}
if (-not (Test-Path -LiteralPath $configuration -PathType Leaf)) {
    throw 'Falta desktop.json. Prepare la configuración de la instalación antes de continuar.'
}
if (Test-Path -LiteralPath $recovery) {
    throw 'Ya existe recovery. No se reemplaza ni repara un diario existente; investigue antes de continuar.'
}

$process = Start-Process -FilePath (Resolve-Path -LiteralPath $DesktopExe).Path -ArgumentList '--aprovisionar-diario-caja' -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw 'No se pudo crear y verificar el diario protegido. Caja debe permanecer bloqueada; no borre recovery para reintentar.'
}
Write-Output 'Diario de caja creado y verificado para la cuenta Windows actual.'
