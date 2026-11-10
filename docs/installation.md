# Instalación inicial — El Carbonazo

## Primera instalación de producción

1. Configure `ConnectionStrings__DefaultConnection`, `Jwt__Key` (Base64, al menos 32 bytes aleatorios), issuer/audience y `PasswordHashing__WorkFactor` fuera de Git.
2. Con credenciales del rol administrativo de migración, aplique el esquema antes de iniciar la API:

   ```powershell
   dotnet ef database update
   ```

3. La migración conserva la fila histórica `admin` solo cuando coincide exactamente con el seed conocido; la deja inactiva y reemplaza su hash por un marcador no autenticable. No elimina usuarios existentes.
4. Ejecute el bootstrap interactivo una sola vez con la misma configuración de conexión:

   ```powershell
   .\ElCarbonazo.Api.exe --bootstrap-admin
   ```

   Ingrese nombre y email de la Jefa y escriba la contraseña dos veces en la consola. La contraseña no se muestra ni se guarda en archivos. El proceso solo acepta una tabla `usuario` vacía o la fila heredada neutralizada; una segunda ejecución se rechaza.
5. Retire del entorno del comando las credenciales administrativas de migración y ejecute la API con el rol PostgreSQL runtime de privilegios mínimos.

No inicie la API de producción antes de aplicar migraciones y crear la Jefa. No use SQL manual para dar de alta cuentas.

## Rol de ejecución y auditoría

La API debe usar un rol PostgreSQL de ejecución distinto del rol de migraciones. Al provisionar ese rol, no conceda `CREATE`, `ALTER`, `DROP` ni propiedad del esquema. Para `evento_auditoria`, conceda solo lectura/inserción y el uso de su secuencia:

```sql
REVOKE UPDATE, DELETE, TRUNCATE ON public.evento_auditoria FROM PUBLIC;
GRANT SELECT, INSERT ON public.evento_auditoria TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.evento_auditoria_id_evento_seq TO elcarbonazo_runtime;
```

La aplicación además instala triggers que rechazan `UPDATE`, `DELETE` y `TRUNCATE`; la tabla se conserva al menos cinco años. Archivo y purga son tareas administrativas externas con credenciales separadas, respaldo previo y procedimiento documentado; nunca se ejecutan desde la API.

Los logs técnicos se escriben en JSON a consola y a `%ProgramData%\ElCarbonazo\api-logs` en producción, rotan por día, se limitan a 100 MB por archivo y se retienen 30 días. `Logging__FileDirectory` permite configurar otra ruta local con ACL restringidas.

## Development

Después de aplicar las migraciones, el arranque de la API en `Development` crea o reutiliza la fila heredada neutralizada como `admin/admin123`. Este seeder no se ejecuta en otros entornos y no reemplaza usuarios existentes.

Para ejecutar pedidos 003 en una base local, la API también debe tener aplicado el esquema 003. El arranque no aplica migraciones automáticamente. Ejecutar `dotnet ef database update` desde `src/ElCarbonazo.Api` solo después de confirmar configuración y destino exacto; respaldar primero bases con datos que deban conservarse. La tabla `producto` es parte de esa migración: si falta, el catálogo no puede consultarse. El catálogo ficticio se carga por separado mediante `--seed-demo-catalog`, solo en Development, con `DemoCatalog:Enabled=true` y base llamada `elcarbonazo_demo_*` o `elcarbonazo_test_*`. No habilitar el seed ni ejecutar migraciones sobre una base persistente sin autorización y destino confirmado.

## Provisión prevista para operaciones recuperables

**Pendiente de implementación y verificación:** al desplegar el módulo 002, el instalador deberá crear el diario inicial `recovery\journal.json` bajo `%ProgramData%\ElCarbonazo`, con estado `SinPendientes` e identificador de instalación coincidente. En actualización no sobrescribirá un diario existente. Si falta o está corrupto, no se debe crear uno vacío para desbloquear la terminal; se requiere recuperación administrativa segura. Las ACL y el reemplazo atómico se verificarán con las cuentas Windows de operación. El procedimiento detallado y los permisos de las nuevas tablas se publicarán junto con la implementación, no se consideran disponibles todavía.

### Caja 004: preparación pendiente

El almacenamiento de desarrollo requiere `recovery\caja-journal.json` y `recovery\caja-journal.json.lock` preexistentes. El primero debe tener versión 1, UUID de instalación coincidente, estado `SinPendientes` y campos de operación, propietario y tipo explícitamente nulos; el segundo se usa para exclusión entre procesos. En una actualización o recuperación no se debe reemplazar un diario existente ni crear uno vacío para omitir una incertidumbre. No incluir credenciales, importes o referencias externas.

Todavía no existe instalador ni política registrada que verifique propietario, ACL y rutas padre en Windows. Antes de habilitar caja Desktop debe definirse la identidad o grupo operativo autorizado para la instalación, restringir ambos archivos, temporales y directorio, protegerlos contra herencia o accesos no autorizados y probar con cuentas reales autorizadas y no autorizadas que el reemplazo preserva la protección. No considerar el validador de formato ni las pruebas con política ficticia como prueba de ACL. Si no se puede verificar la protección y la persistencia, bloquear mutaciones recuperables.
