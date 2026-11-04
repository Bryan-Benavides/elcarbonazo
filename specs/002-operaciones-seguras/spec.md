# Especificación 002 — Operaciones de mesa recuperables

**Estado: propuesta pospuesta para una etapa futura de endurecimiento. No bloquea el prototipo universitario 001 y no está implementada. Su adopción para producción requiere revisión y aprobación vigentes.**

## Objetivo y alcance

Permitir que Desktop determine de forma segura el resultado de cambios de estado de mesa, creación de grupos y cierre de grupos cuando se pierde la respuesta. La API sigue siendo la autoridad de negocio; no se agregan pedidos, pagos ni gestión de personal. La operación sigue siendo LAN y Desktop conserva el aislamiento REST.

Un GET de mesas refleja una fotografía, pero no demuestra que una escritura anterior haya terminado. Nunca se habilitará una operación equivalente basándose únicamente en esa fotografía.

## Requisitos funcionales

1. Cada intención humana confirmada usa un identificador de operación UUID generado por Desktop antes de cualquier envío. Antes de ejecutarla, Desktop registra la intención en la API mediante un paso explícito de preparación; la preparación por sí sola no cambia mesas ni ejecuta la operación.
2. La API vincula el identificador inmutablemente con el usuario autenticado, el tipo de operación, la versión del contrato y los argumentos tipados exactos. La identidad de instalación es metadato y no constituye autenticación. Repetir una preparación idéntica con el mismo identificador no crea otra operación; reutilizarlo con otros argumentos, tipo o propietario se rechaza con conflicto.
3. La ejecución de una operación preparada requiere autenticación y autorización vigentes, ejecuta como máximo una vez el cambio de negocio y aplica las validaciones actuales de roles, versiones, pedidos pendientes y auditoría. Una solicitud duplicada nunca repite el cambio y, si ya terminó, devuelve el resultado registrado.
4. Los resultados terminales son `Completada`, `Rechazada` o `Cancelada`; `Preparada` significa que no existe resultado terminal observable y **no demuestra** que no haya una ejecución concurrente. La respuesta terminal conserva status HTTP y datos mínimos suficientes para reproducir el resultado o el código de rechazo; no conserva tokens ni contraseñas.
5. La consulta autenticada de estado muestra solo operaciones autorizadas. `No encontrada` no prueba que una preparación o ejecución retardada no pueda llegar posteriormente; Desktop mantiene la incertidumbre y no crea una intención equivalente automáticamente.
6. Desktop puede solicitar la cancelación por identificador incluso si la preparación no es observable: la API establece una marca terminal `Cancelada` asociada al usuario que solicita la cancelación y al UUID, que impide una preparación o ejecución posterior con ese UUID. La API serializa preparación, cancelación y ejecución por identificador: si la cancelación gana, toda ejecución posterior es rechazada; si la ejecución gana, la cancelación espera o responde sin afirmar cancelación hasta conocer el resultado terminal. No existe cancelación que revierta una operación ya confirmada. Solo el propietario, Jefa o Supervisor pueden consultar o solicitar cancelación de una operación existente; otros usuarios no conocen su existencia ni pueden modificarla.
7. Si el proceso o la conexión falla durante una ejecución, el cambio funcional, su evento auditable y el resultado terminal exitoso se confirman en una única transacción o ninguno se confirma. La resolución de errores y rechazos debe evitar dejar un resultado terminal que contradiga un commit. Las solicitudes tardías conservan el mismo identificador y no pueden ejecutar después de una cancelación terminal.
8. Desktop conserva antes del primer envío únicamente el identificador y los metadatos mínimos no secretos necesarios para recuperar operaciones pendientes tras reinicio, en almacenamiento local con permisos apropiados. Nunca persiste access/refresh tokens, contraseñas ni secretos. El fallo de persistencia local impide enviar una nueva operación recuperable.
9. Mientras una operación no tenga estado terminal, Desktop conserva las mesas visibles en lectura y bloquea mutaciones que puedan entrar en conflicto; no encola nuevas operaciones offline. Una consulta de estado o GET nunca se considera actividad humana ni renueva automáticamente la sesión. Tras iniciar sesión, la recuperación se limita a operaciones que ese usuario está autorizado a consultar.
10. La política inicial es conservadora: mientras exista una operación local no resuelta se bloquean todas las mutaciones de mesas de esa terminal. La lectura y la consulta de estado continúan si la sesión y la red lo permiten. Un operador autorizado puede solicitar cancelación y esperar su resultado; no se desbloquea por tiempo transcurrido ni por un GET de mesas.
11. Con resultado terminal, Desktop obtiene `GET /api/mesas` satisfactorio antes de desbloquear acciones; muestra el resultado real. Un conflicto de versión o rechazo de dominio exige nueva decisión humana con un UUID nuevo, nunca una repetición automática de la intención fallida.
12. Los registros de resultados completos tienen retención configurable con mínimo de 30 días después del resultado terminal. Tras vencerla, se conserva indefinidamente una marca mínima de identificador, propietario, huella cuando exista, y estado terminal, suficiente para rechazar reutilización y evitar ejecución tardía. Una cancelación que ganó antes de preparar puede no tener huella. No se purgan operaciones sin resultado terminal. El contrato debe definir el resultado explícito `resultado archivado` cuando ya no se conserva el detalle; este nunca autoriza ejecutar de nuevo.
13. El propietario con sesión vigente o un usuario con rol Jefa o Supervisor puede consultar y solicitar cancelación de una operación pendiente. Jefa/Supervisor no pueden ejecutar una intención ajena ni alterar el propietario o la auditoría original. La cancelación supervisada es una acción humana explícita, confirmada y auditada con actor supervisor, UUID, propietario cuando sea conocido y resultado real; el registro auditable comparte transacción con la barrera. Consultas ordinarias no generan auditoría por cada polling. Repetir la cancelación de una operación ya cancelada no genera una segunda mutación ni auditoría de cancelación exitosa.

## Contrato funcional

- `PUT /api/operaciones/{id}` prepara una intención tipada; requiere Bearer y UUID válido. Devuelve la representación autorizada de la operación. Una preparación repetida e idéntica devuelve el mismo registro; una discordancia devuelve 409 `OPERACION_CLAVE_EN_USO` sin revelar datos ajenos.
- `POST /api/operaciones/{id}/ejecucion` ejecuta la intención ya preparada, sin aceptar argumentos de negocio alternativos. Una respuesta satisfactoria o de dominio devuelve el resultado de la operación registrada. Los errores de autenticación/autorización se evalúan en cada solicitud; no se usa el UUID como credencial.
- `GET /api/operaciones/{id}` consulta estado, resultado permitido y código de rechazo. Respuestas para inexistencia y falta de permiso no permiten enumerar operaciones ajenas.
- `POST /api/operaciones/{id}/cancelacion` intenta establecer la barrera terminal `Cancelada`; una respuesta terminal indica el estado real que ganó la carrera. Los timeouts de este endpoint también se consideran inciertos y se resuelven consultando el mismo UUID.

La intención tipada admite exactamente: cambio de estado con ID/estado/version, creación con 2–15 IDs/versiones, o cierre con ID de grupo. La codificación canónica, límites de tamaño, respuestas HTTP exactas, índices, retención y compatibilidad con endpoints previos se definen en `plan.md`.

## Pruebas y criterios de aceptación

- Múltiples ejecuciones concurrentes del mismo UUID producen una sola mutación y un solo evento auditable; cambio de argumentos o propietario se rechaza.
- Probar carrera preparación/consulta, ejecución/cancelación, ejecución duplicada y respuestas retardadas, incluyendo que `Preparada` y `No encontrada` no sean interpretadas como finalización segura.
- Inyectar fallo antes del commit, después del commit y durante respuesta; tras reinicio la operación se resuelve sin duplicar ni contradecir el estado de dominio.
- Probar recuperación Desktop después de cierre, falta de red, sesión expirada y nuevo login del mismo usuario; ningún secreto se escribe en el registro local. Verificar aislamiento entre usuarios.
- Verificar que Jefa/Supervisor puedan resolver una operación ajena sin ejecutarla, incluso cuando aún no exista preparación visible; que Cocina y otros usuarios ajenos no puedan consultarla ni cancelarla; y que cada barrera creada en intervención supervisada quede auditada con el actor correcto.
- Verificar conflictos, pedidos pendientes, grupos y restricciones de rol contra PostgreSQL temporal aislado; cubrir auditoría transaccional y compatibilidad REST.
- Verificar que una marca archivada impida ejecución tardía/reutilización, y que ninguna operación pendiente sea eliminada por limpieza.

## Dependencias y estado de T5

Esta propuesta futura modificaría el contrato de mutaciones y requeriría migración, API y Desktop. El prototipo universitario 001 usa los endpoints actuales y puede cerrar T5 dejando explícito que un GET no garantiza la finalización de una escritura incierta.
