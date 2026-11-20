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

### 006: primera provisión HMAC — solo instalación autorizada

El comando `--provisionar-hmac-autorizacion` realiza únicamente la primera provisión del registro HMAC de 006. No aplica migraciones ni se ejecuta al iniciar la API. Se probó su ejecución como proceso separado contra PostgreSQL temporal; la autorización superior completa de 006 aún no está implementada.

Antes de usarlo en una base persistente, confirmar expresamente el destino y obtener autorización. Aprovisionar una clave aleatoria independiente de JWT, de al menos 32 bytes, desde una fuente segura externa a Git; configurar su Base64 mediante `AutorizacionSuperior__ClaveHmac` y mantenerlo protegido y estable entre reinicios. No imprimirlo, escribirlo en archivos versionados ni pasarlo como argumento de línea de comandos. La longitud no demuestra por sí sola que una clave sea aleatoria. Configurar también la conexión administrativa y la clave JWT existente mediante sus fuentes protegidas.

Desde la raíz ejecutar `dotnet run --project src/ElCarbonazo.Api -- --provisionar-hmac-autorizacion`. El comando muestra la base, dirección y puerto efectivos de PostgreSQL y exige escribir exactamente la confirmación indicada. Si difieren del destino autorizado, cancelar; no reutilizar una confirmación de otra instancia. La provisión solo acepta la fila inicial existente, sin operaciones/autorizaciones/contadores 006 previos, y audita el cambio. Repetirla no reemplaza la clave ni omite la espera de rotación. Retirar después las credenciales administrativas del entorno y ejecutar la API con rol runtime.

Si falta la fila HMAC, no crear ni sobrescribir el registro manualmente. El usuario aprobó un comando administrativo explícito para ese caso: tras confirmar la base y respaldar/preservar evidencia, `dotnet run --project src/ElCarbonazo.Api -- --recuperar-hmac-autorizacion` exige la confirmación específica mostrada, registra auditoría y bloquea autorizaciones durante 24 horas sin borrar contadores ni operaciones. Requiere la misma configuración protegida de clave y conexión. No ejecutar contra base persistente sin autorización y destino confirmados. Todas las instancias que usen esa base deben estar detenidas o usar una versión que respete la misma barrera HMAC; no se ha validado una actualización mixta.

Si la fila existe, está dañada de otra forma, hay un fallo o el resultado de recuperación es incierto, no repetir ni reparar con SQL manual: conservar evidencia y mantener la autorización bloqueada hasta revisión. El comando para fila ausente no constituye una restauración integral ni resuelve otros daños; no se ha validado una restauración de respaldo ni relojes de múltiples equipos.

**Pendiente de implementación y verificación:** al desplegar el módulo 002, el instalador deberá crear el diario inicial `recovery\journal.json` bajo `%ProgramData%\ElCarbonazo`, con estado `SinPendientes` e identificador de instalación coincidente. En actualización no sobrescribirá un diario existente. Si falta o está corrupto, no se debe crear uno vacío para desbloquear la terminal; se requiere recuperación administrativa segura. Las ACL y el reemplazo atómico se verificarán con las cuentas Windows de operación. El procedimiento detallado y los permisos de las nuevas tablas se publicarán junto con la implementación, no se consideran disponibles todavía.

### Caja 004: primera instalación manual de demostración

El almacenamiento de desarrollo requiere `recovery\caja-journal.json` y `recovery\caja-journal.json.lock` preexistentes. El primero debe tener versión 1, UUID de instalación coincidente, estado `SinPendientes` y campos de operación, propietario y tipo explícitamente nulos; el segundo se usa para exclusión entre procesos. En una actualización o recuperación no se debe reemplazar un diario existente ni crear uno vacío para omitir una incertidumbre. No incluir credenciales, importes o referencias externas.

Desktop registra una política conservadora de comprobación de propietario, ACL y rutas padre en Windows. Para la demostración aislada se utiliza la cuenta Windows actual como operadora; no se supone que otras cuentas estén autorizadas. En carpeta temporal real se comprobó una configuración restringida de la cuenta actual, conservación de la protección tras reemplazos y rechazo al conceder acceso amplio. Por decisión del usuario no se exige otra cuenta Windows real para esta demostración; su acceso efectivo no se ha probado. No considerar las pruebas con política ficticia como prueba de ACL. Si no se puede verificar la protección y la persistencia, bloquear mutaciones recuperables.

En una **primera instalación nueva**, tras preparar `%ProgramData%\ElCarbonazo\desktop.json` con URL válida e `idInstalacion` UUID, ejecute desde PowerShell con la misma cuenta Windows que usará Desktop:

```powershell
.\scripts\instalar-diario-caja.ps1 -DesktopExe 'C:\ruta\al\ElCarbonazo.Desktop.exe'
```

El script invoca `--aprovisionar-diario-caja`, espera su resultado y no inicia la interfaz. El comando lee la configuración y crea `recovery` solo si no existe; verifica el diario, el UUID y la política de seguridad antes de devolver éxito. Si falla, no borre `recovery` ni vuelva a crear el diario para desbloquear caja: investigue y conserve la evidencia. Tampoco lo ejecute en una actualización o instalación existente sin confirmar el destino y resolver cualquier operación pendiente. La prueba automatizada ejercitó el servicio y ACL con archivos temporales; además, con autorización, el script se ejecutó en este equipo para la demostración. No hay instalador empaquetado validado.

### Anulaciones 006: primera instalación manual de demostración

La anulación de pedidos requiere el diario protegido `recovery\anulaciones\anulaciones-journal.json` y su archivo `anulaciones-journal.json.lock`, ambos bajo `%ProgramData%\ElCarbonazo`. El diario guarda únicamente versión, UUID de instalación, estado, UUID de operación y propietario; no contiene motivo, credenciales, token ni cuerpo de la petición. Si el diario falta o no se puede verificar, la vista de salón deshabilita la anulación y **no** crea un diario vacío para desbloquearla.

La provisión es explícita y de una sola vez. Requiere que `recovery` ya exista (por ejemplo, provisto con el flujo de caja) y que `recovery\anulaciones` no exista. Con la misma cuenta Windows que usará Desktop:

```powershell
.\scripts\instalar-diario-anulaciones.ps1 -DesktopExe 'C:\ruta\al\ElCarbonazo.Desktop.exe'
```

El script invoca `--aprovisionar-diario-anulaciones`, espera su resultado y no inicia la interfaz. El comando crea la carpeta con ACL restringida a la cuenta actual, Administradores y Sistema, verifica la protección del diario y del lock, y lo deja en estado `SinPendientes`. Una segunda ejecución falla sin sobrescribir el diario existente, incluso si quedó una operación pendiente. Si falla, no borre `recovery\anulaciones` ni fabrique un diario manualmente: investigue y conserve la evidencia. No lo ejecute en una actualización sin confirmar el destino y conciliar cualquier operación pendiente. La prueba automatizada ejercita la creación, la negativa a sobrescribir y la política Windows con archivos temporales; no hay instalador empaquetado validado.
