# Despliegue LAN de producción

## API y TLS

1. Asigne al servidor un nombre local estable `elcarbonazo-api.local` mediante DNS local o el archivo `hosts` administrado en los equipos. Reserve su dirección en DHCP; no compile una IP en la aplicación.
2. Emita un certificado de una CA local con SAN `DNS:elcarbonazo-api.local`. Instale la raíz/intermedia de esa CA en el almacén de confianza de cada terminal Windows y el certificado/clave privada en el servidor. No desactive la validación TLS en clientes.
3. Copie `deploy/api-production.example.json` fuera del repositorio a la ubicación protegida de configuración del servicio. Sustituya todos los marcadores; genere `Jwt:Key` con CSPRNG, codificación Base64 y al menos 32 bytes aleatorios. Configure la cadena PostgreSQL y secretos por variables de entorno/secret store, nunca en argumentos de proceso ni logs.
4. Configure el servicio Windows con `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=https://0.0.0.0:7191`, `Kestrel__Certificates__Default__Path` y `Kestrel__Certificates__Default__Password` suministrados desde almacenamiento protegido. Dé lectura del certificado y configuración solo a la identidad del servicio.
5. Permita TCP/7191 en Windows Firewall únicamente para el perfil `Private` y el rango LAN del restaurante. PostgreSQL debe escuchar en loopback si reside en el mismo equipo; si está en otro servidor, permita 5432 solo desde el host API.
6. Configure una carpeta local de logs con ACL para administradores y la identidad API. La aplicación escribe JSON rotativo en consola y archivo; consola permanece disponible aunque un archivo alcance 100 MB.

La API de producción no aplica migraciones al arrancar. Aplique `dotnet ef database update` desde `src/ElCarbonazo.Api` con credenciales administrativas de migración antes de iniciar el servicio. Ejecute luego el servicio con `elcarbonazo_runtime`, sin `CREATE`, propiedad de esquema ni permisos DDL. El comando interactivo `--bootstrap-admin` se ejecuta durante instalación con credenciales temporales de bootstrap; retire esas credenciales antes de habilitar la API.

## Desktop y configuración local

El instalador crea `%ProgramData%\ElCarbonazo\desktop.json` con el formato de `deploy/desktop.example.json`, generando `idInstalacion` una sola vez con UUID aleatorio. ACL: usuarios normales solo leen; instalador/administradores escriben. Producción no regenera identificadores faltantes ni permite editar el endpoint desde LoginView. `ApiBaseUrl` debe usar el hostname TLS confiable; override por entorno queda limitado a Development/pruebas.

**Operaciones recuperables 002 — pendiente de implementación:** la futura instalación deberá provisionar y proteger un diario local permanente conforme a `specs/002-operaciones-seguras/plan.md`. No borrar ni reinicializar el diario durante reparaciones. El cambio requiere migración y actualización coordinada de API y Desktop; los endpoints de escritura antiguos dejarán de estar disponibles solo cuando ese despliegue esté listo y verificado.

## PostgreSQL runtime (permisos mínimos)

Provisionar los roles `elcarbonazo_runtime` y `elcarbonazo_bootstrap` fuera del proceso de migración y aplicar `deploy/runtime-grants.sql` como administrador. El rol runtime solo recibe operaciones DML requeridas por la API; el bootstrap solo puede leer/crear/actualizar `usuario` y agregar auditoría. No conceda `CREATE`, propiedad ni permisos DDL. Para auditoría el runtime recibe exclusivamente `SELECT`/`INSERT` y `USAGE` de su secuencia; no recibe `UPDATE`, `DELETE` o `TRUNCATE`. La auditoría tiene trigger append-only adicional.

## Respaldo y restauración

- Una tarea diaria ejecuta `pg_dump --format=custom` con un `PGPASSFILE` de ACL restrictiva o almacén equivalente; nunca pase la contraseña en línea de comandos.
- Cifre el archivo antes de copiarlo a un segundo disco o NAS local separado del servidor. Conserve 30 días y supervise que la tarea termine correctamente.
- Una vez al mes restaure el dump en una base aislada cuyo nombre empiece `elcarbonazo_restore_test_`; valide migraciones, conteos y relaciones sin reemplazar ni escribir producción.
- Mantenga por al menos cinco años los eventos de auditoría. Archivo/purga se realiza por un administrador separado, con respaldo previo y credenciales que no se configuran en la API.

## Prueba de aceptación sin Internet

Con Internet desconectado, desde una segunda terminal LAN: resolver `elcarbonazo-api.local`, comprobar cadena/SAN del certificado, iniciar sesión y consultar `/api/mesas`; verificar que no hay conexión directa desde Desktop a PostgreSQL y que ningún endpoint/configuración intenta acceder a servicios externos.
