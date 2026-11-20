# Instrucciones de entrega — Sistema POS "El Carbonazo"

Este paquete contiene el sistema de punto de venta listo para ejecutar en su computadora (Windows).

## Qué incluye
1. Código fuente completo del proyecto (.NET 10).
2. Ejecutables publicados para Windows (API y Escritorio).
3. Base de datos ya preparada con menú, ingredientes, recetas y mesas.

## Antes de empezar
Necesita instalado en su computadora:
- **.NET 10 SDK** (https://dotnet.microsoft.com)
- **PostgreSQL 15 o superior** (https://www.postgresql.org)

## Paso 1: Crear la base de datos
Abra una terminal en la carpeta del proyecto y ejecute:

```powershell
dotnet ef database update --project src/ElCarbonazo.Api
```

Esto crea todas las tablas necesarias en PostgreSQL.

## Paso 2: Configurar el entorno
Cree un archivo llamado `entorno.ps1` en la raíz del proyecto con este contenido (cambie `SU_CONTRASEÑA` por la contraseña de su PostgreSQL):

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=elcarbonazo;Username=postgres;Password=SU_CONTRASEÑA'
$env:Jwt__Key = 'BASE64_DE_AL_MENOS_32_BYTES_ALEATORIOS'
$env:AutorizacionSuperior__ClaveHmac = 'BASE64_DE_AL_MENOS_32_BYTES_ALEATORIOS'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
```

Luego ejecútelo:

```powershell
. .\entorno.ps1
```

## Paso 3: Iniciar el servidor (API)
En una terminal, deje abierta esta ventana:

```powershell
dotnet run --project src/ElCarbonazo.Api --launch-profile https
```

## Paso 4: Iniciar el programa (Escritorio)
En otra terminal:

```powershell
dotnet run --project src/ElCarbonazo.Desktop
```

## Paso 5: Iniciar sesión
Usuario: `admin`  
Contraseña: `admin123`

## Qué puede mostrar
Desde el escritorio podrá:
- Ver las mesas del restaurante
- Crear pedidos y enviarlos a cocina
- Cobrar cuentas en caja (efectivo, tarjeta o transferencia)
- Ver reportes de ventas
- Gestionar inventario y recetas
- Anular ítems con autorización de Jefa

## Notas importantes
- Este es un proyecto académico para presentación universitaria.
- No está diseñado para operación comercial real ni para uso con dinero real.
- Los datos de la base son de ejemplo.

## Soporte
Si algo no funciona, revise primero que PostgreSQL esté ejecutándose y que la conexión en `entorno.ps1` sea correcta.
