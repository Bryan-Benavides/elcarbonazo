# Validación técnica previa — Operaciones de mesa recuperables

**Estado: análisis estático; garantías pendientes de pruebas sobre PostgreSQL y Desktop.** No sustituye las pruebas de S2–S7 ni acredita que exista una implementación.

## Resultado

El diseño con preparación, UUID global, transacción de operación y barrera de cancelación es viable con ASP.NET Core, EF Core y PostgreSQL existentes, sin añadir infraestructura externa. No es seguro implementar solo la tabla o un wrapper HTTP: las reglas de dominio, el estado de EF y la persistencia local deben participar en el protocolo completo.

## Comprobaciones contra la implementación actual

1. `MesasService.UpdateEstadoAsync` lee, valida y cambia la entidad antes de abrir su propia transacción; `GruposMesaService` abre y confirma su propia transacción para crear/cerrar. S3 debe extraer variantes internas que no abran ni confirmen transacciones y ejecutarlas bajo la transacción del protocolo. No asumir que transacciones anidadas de EF Core proporcionan atomicidad entre operación, negocio y auditoría.
2. La API guarda auditoría mediante `IAuditRecorder` y `AppDbContext`. La cancelación supervisada debe registrar su evento con el mismo contexto y transacción que la barrera; hay que verificar que el modelo de detalles permitidos incluya únicamente UUID, actor/propietario y resultado, sin cuerpos ni secretos.
3. Una PK UUID y `INSERT ... ON CONFLICT DO NOTHING`, seguidos de lectura/bloqueo y revalidación, pueden serializar preparación y cancelación cuando la fila no existía. Se requiere probarlo con **conexiones y transacciones independientes**; un GET aislado en MVCC puede mostrar `Preparada` mientras una ejecución no confirmada mantiene el lock.
4. Una ejecución y una cancelación de fila existente deben adquirir `FOR UPDATE` sobre la operación antes de tocar el dominio. Para grupos se conserva después el orden grupo→mesas por ID documentado en 001; para creación, operación→mesas por ID. Las escrituras legadas deben quedar deshabilitadas en el despliegue coordinado, no durante una fase parcial en la que Desktop aún no pueda operar.
5. Un `DbUpdateConcurrencyException` deja entidades rastreadas en el contexto aunque se revierta la base. S3/S4 deben recuperar el savepoint y limpiar/restaurar explícitamente el seguimiento EF de entidades de negocio antes de persistir `Rechazada`, manteniendo el lock de operación; si no se puede demostrar un estado limpio, revertir y dejar la intención no terminal. Nunca registrar un rechazo fuera de la barrera sin volver a serializar.
6. Una respuesta perdida durante commit no demuestra rollback. La única forma de obtener un resultado concluyente es leer el registro terminal o conseguir una cancelación serializada; un GET de mesas es una fotografía y no sustituye esa resolución. El resultado archivado es terminal, pero no reconstruye el detalle perdido y debe comunicarse como tal.
7. `SessionManager.SendPollingAsync` no realiza refresh por su cuenta; la consulta automática de estado debe usar esa ruta y respetar `Paused`, `Expired` y `SecurityBlocked`. La recuperación supervisada explícita es interacción humana y puede renovar según T4; el UUID y `X-Installation-Id` nunca autorizan al usuario.
8. El almacenamiento local compartido entre cuentas Windows necesita ACL de instalación verificadas, escritura/reemplazo atómico, recuperación ante archivo corrupto y exclusión mutua entre procesos. Una eliminación o modificación externa del diario podría eludir el bloqueo local: el diseño no garantiza integridad frente a un administrador o usuario con permisos indebidos. Ante ausencia inesperada, corrupción o acceso denegado debe fallar cerrado; especificar inicialización distinguible de pérdida del archivo y probarla en Windows.
9. El límite de 30 preparaciones nuevas/minuto debe ser atómico por usuario ante concurrencia y no consumir cupo en una repetición idéntica. No limitar consulta/cancelación con esa política evita bloquear recuperación, pero deja superficie de abuso por UUIDs inexistentes: exigir autorización, límites de recursos y observar crecimiento; no prometer protección integral frente a actores autenticados maliciosos.
10. La barrera creada por supervisor antes de la preparación pertenece al supervisor; la preparación tardía del usuario original recibe respuesta no autorizada/ajena y no ejecuta. Desktop debe resolverla con un supervisor y no considerar ese rechazo como prueba por sí sola. La auditoría conserva al actor que creó la barrera; no debe atribuirle una mutación de mesa que nunca ocurrió.

## Pruebas mínimas que condicionan la aceptación

- Dos ejecuciones concurrentes, preparación/cancelación y ejecución/cancelación en conexiones independientes, con barreras controladas; una mutación y una auditoría como máximo por UUID.
- Fallos antes/después de `SaveChanges`, al guardar auditoría, durante commit y al escribir respuesta; comprobar estado de operación y negocio después de reconectar.
- Conflicto EF después de modificar entidad rastreada; comprobar que persistir rechazo no guarda accidentalmente el cambio.
- Reinicio Desktop en cada fase, journal ausente/corrupto, cuentas Windows y procesos simultáneos, rol cambiado y recuperación supervisada; sin secretos en disco.
- Pruebas de autorización y no enumeración, límites concurrentes, `410` de endpoints anteriores y despliegue coordinado.

## Decisiones de diseño concretadas; pendientes de demostración

- Diario permanente provisionado por instalador, sin creación automática si falta y sin sobrescribir en reparación; ACL, UUID de instalación y estado explícito. Queda pendiente probarlo con cuentas Windows reales y documentar recuperación administrativa segura ante pérdida sin una copia vigente demostrable.
- Cupo con fila de contador por usuario bloqueada en la misma transacción de inserción; cancelación y ejecución no toman ese lock. Queda pendiente probar duplicados, carreras con cancelación y límite con conexiones PostgreSQL independientes.
- Savepoint posterior al lock de operación, rollback y limpieza explícita de seguimiento EF antes de registrar rechazo. Queda pendiente probar la estrategia en integración antes de declarar demostrada la atomicidad.
- Alinear las referencias históricas restantes del módulo 001 y las guías de instalación/despliegue cuando se implemente el protocolo.
