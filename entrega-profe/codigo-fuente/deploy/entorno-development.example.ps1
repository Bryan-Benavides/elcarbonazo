# Plantilla de entorno para la demostracion local (Windows PowerShell).
#
# Copie este archivo fuera de Git, complete los valores y ejecute la copia en la
# misma sesion de PowerShell que iniciara la API:
#
#   . C:\ruta\privada\entorno-development.ps1
#   dotnet run --project src/ElCarbonazo.Api --launch-profile https
#
# Las asignaciones estan comentadas a proposito: ejecutar esta plantilla tal cual
# no cambia nada.

# Conexion local a la base de defensa. Ajuste usuario y puerto si difieren.
# $env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=elcarbonazo;Username=postgres;Password=REEMPLAZAR'

# Clave JWT ya usada por la instalacion, en Base64. No la cambie sin volver a
# iniciar sesion en Desktop.
# $env:Jwt__Key = 'REEMPLAZAR_POR_BASE64_DE_AL_MENOS_32_BYTES'

# Clave independiente de JWT para autorizar anulaciones. Debe tener al menos
# 32 bytes aleatorios, en Base64, y permanecer estable entre reinicios.
# Generela sin imprimirla, por ejemplo:
#   $bytes = New-Object byte[] 32
#   $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
#   try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
#   $env:AutorizacionSuperior__ClaveHmac = [Convert]::ToBase64String($bytes)
# No la escriba en archivos versionados, en la linea de comandos ni en capturas.
# Si la cambia sin seguir el procedimiento, las autorizaciones quedan en espera 24 horas.
# $env:AutorizacionSuperior__ClaveHmac = 'REEMPLAZAR_POR_BASE64_DE_AL_MENOS_32_BYTES'

# Entorno de la API. La validacion de TLS solo se relaja en Development.
# $env:ASPNETCORE_ENVIRONMENT = 'Development'
