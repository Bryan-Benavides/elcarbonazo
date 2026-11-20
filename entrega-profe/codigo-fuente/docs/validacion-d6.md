# Validación D6 — integración y aceptación del módulo 003

## Estado

**D6 y módulo 003 aceptados por el usuario para el entorno local aislado/de demostración de dos terminales.** El usuario confirmó que el recorrido integrado funciona y pidió cerrar el módulo. No se ha aplicado ninguna migración a base persistente. Existe una limitación separada en la API HTTPS configurada en su otra instalación: el usuario aportó SQLSTATE `42P01`, relación `producto` inexistente en `ProductosService.GetPageAsync`, lo que confirma que esa base concreta carece de la migración D1/003. No hace falta migrarla para aceptar el módulo en el entorno aislado; sí será requisito antes de usar Pedidos contra esa base. El nombre de base no se ha confirmado y no se ha tocado. No se hizo prueba física LAN ni se midieron latencias de render.

## Migración, compatibilidad y aislamiento

La suite API usa PostgreSQL temporal `elcarbonazo_test_<uuid>` y limpia únicamente la base creada por cada fixture. `PedidosMigrationCompatibilityTests` actualiza desde el punto de esquema 001 `20261103050000_EnforceAuditAppendOnly`, conserva la cuenta activa legado `Listo` y verifica que no se fabrique comanda ni se pueda abrir cuenta activa duplicada. Otro test crea una tabla `producto` incompatible en la base temporal, exige que la migración falle y comprueba que la tabla marcador siga intacta y que no exista esquema parcial `comanda`.

**Límite de configuración en la instalación HTTPS del usuario (no bloquea la aceptación del código 003 en entorno aislado):** las pruebas anteriores verifican migraciones en bases temporales; no migran la base a la que apunta la API HTTPS interactiva. La excepción actual demuestra que esa base está atrasada respecto a 003. Si en el futuro se desea usar Pedidos contra ella, primero confirmar el nombre de base y autorización del destino. `dotnet ef database update` se ejecuta desde `src/ElCarbonazo.Api` y aplica el esquema a la base determinada por la configuración efectiva; no ejecutar hasta confirmar destino y respaldo apropiado. Después, el catálogo demo requiere comando explícito, entorno Development, `DemoCatalog:Enabled=true` y nombre `elcarbonazo_demo_*` o `elcarbonazo_test_*`; no se siembra en una base arbitraria.

La misma suite cubre persistencia, permisos runtime, límites, auditoría/rollback, carreras, idempotencia después de reinicio, respuesta perdida y timeout después de commit. Estas pruebas controladas no equivalen a desconexión física LAN.

## Secuencia final ejecutada — 9 de noviembre de 2026

- `dotnet format ElCarbonazo.sln --verify-no-changes --no-restore`: correcto.
- `dotnet build ElCarbonazo.sln`: correcto, 0 advertencias y 0 errores.
- API: 50/50 pruebas aprobadas.
- Desktop: 141/141 pruebas aprobadas, incluidas pausa de sesión, ausencia de polling durante la pausa, permisos, conflictos y resultado incierto del KDS.
- Auditoría `dotnet list ElCarbonazo.sln package --vulnerable --include-transitive`: ningún paquete vulnerable reportado por los orígenes actuales.
- AcceptanceHost compilado y formateado por separado; `--pedidos-smoke` pasó con base temporal y limpió su entorno. Verificó flujo REST de salón y Cocina hasta `Lista` y lectura de cuenta. Usa sesión adaptada, no instancia la ventana ni mide render.
- EF Core emitió dos `MultipleCollectionIncludeWarning`; su impacto no ha sido medido. Ver ERR-018.

## Ajuste encontrado durante el cierre

La revisión de D6 encontró que KDS informaba sesión pausada sin suspender las consultas periódicas. Se corrigió para detener polling hasta que la sesión vuelva a estar activa y se añadió una prueba que confirma que no se hacen lecturas durante la pausa. Ver ERR-020.

## Validación manual informada

El usuario confirmó D4/D5 y el recorrido de dos terminales, incluido preparación → lista, desaparición de cola y estado listo en salón, y declaró oficialmente terminado el módulo 003. La captura confirmó visualmente el acceso Cocina en la barra lateral. Una configuración HTTPS distinta sigue sin tabla `producto`; se documenta como requisito de provisión futura para usar Pedidos allí, no como motivo para alterar ahora una base persistente ni invalidar el recorrido aislado aceptado.

Si se necesita Pedidos en la API HTTPS habitual, migrar y preparar catálogo en una base autorizada que cumpla el prefijo de demo; confirmar nombre y respaldo antes de hacerlo. Continúan fuera de alcance: operación LAN física, recuperación durable de intenciones, inventario, caja/pagos, facturación, anulación de líneas y medición cronometrada de render. No se declara aceptación para producción.
