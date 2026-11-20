param(
    [Parameter(Mandatory = $true)]
    [string] $DesktopExe
)

$ErrorActionPreference = 'Stop'
$applicationDirectory = Join-Path $env:ProgramData 'ElCarbonazo'
$configuration = Join-Path $applicationDirectory 'desktop.json'
$recovery = Join-Path $applicationDirectory 'recovery'
$journal = Join-Path $recovery 'anulaciones'

if (-not (Test-Path -LiteralPath $DesktopExe -PathType Leaf)) {
    throw 'No se encontró el ejecutable Desktop indicado.'
}
if (-not (Test-Path -LiteralPath $configuration -PathType Leaf)) {
    throw 'Falta desktop.json. Prepare la configuración de la instalación antes de continuar.'
}
if (-not (Test-Path -LiteralPath $recovery -PathType Container)) {
    throw 'Falta la carpeta recovery. Provisiónela primero con el flujo de caja autorizado; no se crea automáticamente.'
}
if (Test-Path -LiteralPath $journal) {
    throw 'Ya existe recovery\anulaciones. No se reemplaza ni repara un diario existente; investigue antes de continuar.'
}

$process = Start-Process -FilePath (Resolve-Path -LiteralPath $DesktopExe).Path -ArgumentList '--aprovisionar-diario-anulaciones' -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw 'No se pudo crear y verificar el diario protegido de anulaciones. Las anulaciones deben permanecer bloqueadas; no borre recovery\anulaciones para reintentar.'
}
Write-Output 'Diario de anulaciones creado y verificado para la cuenta Windows actual.'
