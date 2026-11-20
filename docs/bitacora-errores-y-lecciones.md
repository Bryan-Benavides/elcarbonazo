# Bitácora de errores y lecciones — El Carbonazo

### 006: botones del modal de anulación deshabilitados

- **Hecho observado:** el usuario abrió el modal y no pudo usar Cancelar ni Confirmar. `PuedeCancelarAnulacion` no notificaba sus cambios al abrir el modal o cambiar el estado de ejecución.
- **Corrección:** se notifican las dependencias del botón Cancelar y los campos actualizan sus bindings al escribir. Una prueba de ViewModel verifica notificaciones, validación y cancelación sin envío. Compilación y pruebas focalizadas pasaron; queda pendiente comprobar los botones y la anulación en ventana real.

### Código vigente frente al esquema 005 sin migración 006

- **Hecho confirmado por revisión:** `EnviosService` añade filas a `consumo_detalle`, tabla creada por `AddAuditoriaCancelacionesSeguridad`; otros servicios y entidades utilizan campos añadidos por esa migración. La migración posterior de reservaciones también sigue a 006 en el historial EF.
- **Contexto:** inicialmente se aplicaron a la base local `elcarbonazo` solo las migraciones hasta 005. Tras informar de la dependencia, el usuario autorizó explícitamente la migración de esquema 006 y reservaciones; EF confirmó ambas aplicadas. El SQL generado de las primeras tres migraciones no contenía `DROP TABLE`, `DROP COLUMN`, `TRUNCATE` ni eliminación de pedidos; no se verificó aún un recorrido de negocio completo contra esa base.
- **Control:** el catálogo se cargó después de pruebas en PostgreSQL temporal; el smoke con ese catálogo verificó pedidos, Cocina, Caja y consumo único en entorno temporal. No afirmar que aplicar el esquema completa o acepta anulaciones. No fabricar manualmente tablas ni editar migraciones históricas. El recorrido visual con la base persistente aún no se ha comprobado.

### Reservaciones: historial cancelado en la agenda

- **Hecho observado por el usuario:** tras cancelar, la agenda seguía mostrando la reserva con el botón «Cancelar», aunque el registro había cambiado de estado y la mesa estaba disponible.
- **Control:** listar únicamente reservas activas antes del límite de 500, conservando los registros cancelados y su auditoría en PostgreSQL; Desktop también filtra por estado.

### Reservaciones: notificación y resultado incierto

- **Hecho confirmado:** los botones dependían de `PuedeEscribir`, pero los cambios de ocupación e incertidumbre no notificaban esa propiedad. La consulta de agenda posterior a un rechazo también reemplazaba su mensaje.
- **Control:** notificar el cambio, conservar el mensaje de la operación y bloquear nuevas escrituras ante un resultado incierto. Una consulta no demuestra que la escritura anterior haya terminado. El bloqueo de esta pantalla es solo de la sesión: no garantiza recuperación tras reinicio, otro usuario o terminal.
- **Evidencia:** pruebas ViewModel simulan desconexión y comprueban un único envío y el bloqueo; no sustituyen revisión visual.

### 006: `numeric` y comparación positiva con NaN

- **Hecho confirmado en PostgreSQL temporal:** `'NaN'::numeric > 0` devuelve verdadero, de modo que el CHECK original `cantidad > 0` no excluía ese valor especial.
- **Control:** los nuevos CHECK de cantidad de snapshot e importe de anulación rechazan explícitamente `NaN`; las pruebas ejercitan el rechazo con FKs existentes. Esto no acredita que todas las columnas numéricas anteriores estén protegidas frente a valores especiales.

### 006: CHECK de resultado terminal insuficiente

- **Hecho confirmado por revisión:** el CHECK inicial de `operacion_anulacion` exigía un JSON no nulo y un status presente, pero admitía JSON `null` o escalar y status incompatibles con el estado terminal.
- **Control:** la migración 006 en desarrollo y el modelo exigen objeto JSON, código no vacío, fecha terminal coherente y rangos HTTP por estado. Las pruebas PostgreSQL rechazan esos casos; las transiciones terminales y el acuse/consumo único se protegen además con triggers. Esto no sustituye las validaciones y pruebas transaccionales de C2.

### 006: modelo EF actual contra migración histórica

- **Hecho confirmado:** al añadir columnas de 006, dos pruebas que poblaban esquemas anteriores a 006 mediante el modelo EF actual fallaron porque `comanda.revision_detalles` aún no existía en esos esquemas.
- **Control:** construir los datos previos con un contexto de prueba que ignore las propiedades posteriores y usar el contexto normal para aplicar migraciones; conservar la comparación de históricos. Las pruebas focalizadas pasaron después de ese cambio. No editar migraciones antiguas para adaptar las pruebas.

### 004: historial de caja con esquema ausente

- **Hecho observado en registros locales:** una consulta de `CajaComprobantesController` falló con PostgreSQL `42P01` porque faltaba la relación `cobro`. El registro demuestra el error en esa ejecución, no el estado actual de cualquier otra base o instancia HTTPS.
- **Control:** confirmar la instancia, el destino y las migraciones antes de modificar la base. La API no aplica migraciones automáticamente; no crear tablas manualmente ni aplicar migraciones a una base persistente sin autorización y respaldo.

### 004: consultar un comprobante histórico no es conciliar un cobro

- **Hecho confirmado por revisión:** el detalle histórico hacía dos GET del mismo comprobante y reutilizaba una aplicación que invalidaba la cuenta seleccionada y el turno.
- **Control:** validar la respuesta una vez y aplicar el historial sin modificar la revisión de cobro; conservar la invalidación al conciliar una operación. Las pruebas comprueban una sola consulta de detalle, preservación de selección/turno y descarte al cambiar de sesión.

### 004: resultado JSON durable no conserva el orden textual de propiedades

- **Hecho confirmado en pruebas:** PostgreSQL JSONB puede devolver un resultado reproducido con propiedades reordenadas y espacios distintos al resultado inicial, sin cambiar sus valores.
- **Control:** comparar el contenido JSON semánticamente al verificar reproducción de una operación; no asumir igualdad textual ni alterar el resultado de negocio por diferencias de formato.

### 004: listado informativo sin huella no sirve para confirmar un cobro

- **Hecho confirmado por revisión:** el lector Desktop original mostraba importe y líneas, pero descartaba `huellaLineas`; por tanto, esa fotografía no podía utilizarse para construir el contrato de cobro aprobado.
- **Control:** el lector conserva y valida la huella y exige campos esenciales del detalle para cuentas cobrables. La API sigue revalidando bajo locks; el GET no garantiza que la cuenta permanezca vigente.
- **Evidencia y límite:** pruebas Desktop con respuestas HTTP ficticias rechazan huella o detalle incompletos; queda pendiente la integración de la pantalla con API real y la revisión visual.

### 004: identidad del actor no distingue dos sesiones

- **Hecho confirmado por revisión y prueba con generación simulada:** comprobar únicamente el ID del actor permite confundir la sesión que observó una preparación con otra sesión posterior del mismo usuario.
- **Control parcial:** el coordinador recibe una generación local que cambia al iniciar una sesión nueva y comprueba que no cambió al recibir respuestas, antes de ejecutar una preparación observada o limpiar el diario. El refresh no cambia esa generación.
- **Evidencia y límites:** `CajaRecoveryCoordinatorTests` prueba cambios de generación simulados durante preparación y conciliación; aún falta probar intercalaciones de sesión con el consumidor real y servicios reales. El diario sigue bloqueado si la generación cambia durante esas respuestas.

### 004: autorización después de esperar el lock global

- **Hecho comprobado:** la preparación y ejecución de apertura podían esperar el lock global después de comprobar el rol, sin volver a consultar la autorización antes de persistir.
- **Corrección:** tras adquirir ese lock se vuelve a verificar sesión y rol; el lock se toma fuera del savepoint de dominio para conservarlo durante la persistencia de un rechazo.
- **Evidencia:** pruebas con conexiones PostgreSQL independientes cambian el rol mientras la solicitud está bloqueada y verifican que preparación y ejecución no mutan datos. No demuestra autorización concurrente para handlers C3 aún no implementados.

### 004: transporte recuperable y reintento de sesión

- **Hecho comprobado:** `SessionManager.SendHumanAsync` puede reenviar una solicitud después de refrescar un access token expirado. Ese comportamiento no debe conectarse directamente a mutaciones recuperables de caja, que prohíben reintentos automáticos.
- **Control:** caja usa `SendCajaOnceAsync`, que captura el token de la identidad esperada y no refresca ni reintenta la escritura. Una prueba integra `SessionManager` y el transporte de caja con HTTP ficticio y verifica un solo envío. Falta la prueba extremo a extremo con pantalla y servicios de negocio C3/C4.

### 004: evaluación de ACL heredables en rutas superiores

- **Hecho comprobado:** una carpeta temporal con ACL restringida era rechazada por reglas de directorios superiores de Windows que solo se heredan o permiten crear elementos hermanos. La prueba positiva inicial falló en esta máquina.
- **Causa:** la política trataba permisos que no permiten modificar el directorio protegido existente como si fueran permisos efectivos para sustituirlo.
- **Control:** verificar propietarios de cada ancestro, ignorar ACE de solo herencia únicamente al evaluar ese ancestro y rechazar permisos efectivos de borrado de hijos, eliminación, cambio de permisos o toma de propiedad. La carpeta y archivo del diario siguen exigiendo acceso limitado a identidades confiables. Las pruebas positivas y negativas usan archivos temporales con la cuenta actual; por decisión del usuario no se exige otra cuenta real para la demostración. El acceso efectivo desde otra cuenta no fue validado; el aprovisionamiento sigue pendiente.

### 004: lecturas MVCC y CHECK con valores nulos

- **Hechos comprobados:** en PostgreSQL temporal, una lectura de pendientes realizada por cierre mientras mantiene bloqueado el turno no impide que otra conexión inserte y confirme una operación preparada después de esa lectura. La prueba reproduce la intercalación de persistencia, no un cierre de negocio implementado. Asimismo, un CHECK de payload aceptaba una versión nula.
- **Causas:** una lectura MVCC no bloquea inserciones futuras; en PostgreSQL un CHECK acepta el resultado `UNKNOWN` producido por comparaciones con NULL.
- **Control:** el usuario autorizó revisar el plan: prepare de cobro y cierre comparten una barrera transaccional y revalidan bajo ella; probar ambos órdenes y repetir la prueba en los servicios C2/C3 reales. El CHECK de payload exige explícitamente tipo y versión no nulos para operaciones preparadas; las pruebas incluyen valores nulos y estructura incompatible. No usar una consulta sin coordinación como prueba de ausencia estable de nuevas operaciones.

## Para qué sirve

Esta bitácora conserva errores observados durante el desarrollo, su impacto y controles concretos para reducir la probabilidad de repetirlos. Es una **ayuda de trabajo y revisión**, no garantiza ausencia de errores ni reemplaza las especificaciones, el código, las pruebas o la validación del usuario.

Antes de modificar una parte del proyecto, revisar las entradas relacionadas. Cuando se confirme un error nuevo, agregarlo o actualizar una entrada existente después de investigar su causa. Distinguir siempre **hecho comprobado**, **causa confirmada** y **hipótesis**; si falta evidencia, indicarlo. No incluir contraseñas, tokens, cadenas de conexión, datos personales ni información sensible en ejemplos o trazas.

Las reglas de `docs/constitution.md`, `docs/domain.md` y las especificaciones aprobadas en `specs/` prevalecen sobre esta bitácora. Las propuestas futuras pertenecen a `docs/evolucion-futura-por-modulos.md`; una lección no autoriza introducir funcionalidad sin especificación.

### Formato para nuevas entradas

Copiar este formato y completar solo lo que se haya verificado:

```text
### ERR-NNN — Título breve
- Estado: investigando | corregido | pendiente de validación | cerrado | pendiente
- Contexto y síntoma: qué ocurrió y en qué condiciones.
- Causa: confirmada o hipótesis, indicando la evidencia disponible.
- Corrección: qué se cambió o qué falta decidir.
- Prevención: comprobación concreta para trabajos futuros.
- Evidencia: archivos, prueba, comando o validación; señalar lo que no se verificó.
```

## Interfaz y experiencia

### ERR-001 — Hover de tarjetas anunciado sin comprobación visual
- **Estado:** corregido; comportamiento confirmado visualmente por el usuario.
- **Contexto y síntoma:** al pasar el cursor sobre una tarjeta de mesa no se observaba el efecto esperado, aunque el XAML compilaba.
- **Causa:** el selector aplicado inicialmente dependía de una relación con la plantilla del botón que no produjo el resultado visual esperado. La compilación no demostraba que el estilo alcanzara la tarjeta.
- **Corrección:** se aplicó el estado visual al borde de la tarjeta y se definió una plantilla del botón de mesa para evitar el fondo de hover no deseado.
- **Prevención:** al cambiar estilos Avalonia/SukiUI comprobar selectores, prioridad, plantillas y estados reales; validar hover, selección y foco en ejecución cuando corresponda. No declarar una animación comprobada solo porque compila.
- **Evidencia:** `src/ElCarbonazo.Desktop/Views/MesasView.axaml`; el usuario confirmó que la animación funciona. Ver `docs/validacion-t5.md`.

### ERR-002 — Texto de botones ilegible sobre fondo oscuro
- **Estado:** corregido; nuevos colores confirmados por el usuario.
- **Contexto y síntoma:** en el mapa oscuro, el texto negro de varios botones tenía contraste insuficiente.
- **Causa:** la apariencia heredada de controles no coincidía con las superficies oscuras del mapa; no basta con definir colores de fondo si la plantilla o tema aplica otro color al contenido.
- **Corrección:** se definió un tema para botones del mapa con colores para estados normal, hover, foco y deshabilitado, manteniendo la dirección «Carbón & Fuego».
- **Prevención:** comprobar texto, fondo y estados interactivos en ejecución; calcular contraste de las combinaciones reales y mantener distinción visible entre habilitado y deshabilitado. No asumir que un tema global claro funciona sobre paneles oscuros.
- **Evidencia:** `src/ElCarbonazo.Desktop/Views/MesasView.axaml`; comprobación visual reportada por el usuario. El checklist visual exhaustivo sigue pendiente para producción.

### ERR-003 — Confirmación de liberación poco visible
- **Estado:** corregido; la lógica de cancelar está cubierta por prueba; la revisión visual futura debe conservarse en la aceptación.
- **Contexto y síntoma:** el usuario no identificó la opción de cancelar al intentar verificar la liberación de una mesa. En la captura presentada estaba seleccionada una mesa `Libre`, por lo que `Liberar` estaba deshabilitado; la captura por sí sola no demuestra una causa única del problema con una mesa ocupada.
- **Corrección:** la confirmación de liberación se trasladó a un cuadro central con acciones explícitas de cancelar y confirmar.
- **Prevención:** probar la confirmación con estados elegibles y no elegibles, a tamaño mínimo y con desplazamiento; verificar que cancelar no envíe la mutación y que los mensajes no se confundan con una acción ya ejecutada.
- **Evidencia:** `Views/MesasView.axaml`; `ReleaseRequiresConfirmationAndUsesCurrentVersion` en `MesasPollingLifecycleTests.cs`.

## Sesión, transporte y concurrencia

### ERR-012 — Comprobación de cuenta pendiente fuera del bloqueo de mesa
- **Estado:** corregido y probado con ambas intercalaciones usando conexiones independientes y espera observada en PostgreSQL.
- **Contexto:** liberar una mesa consultaba pedidos pendientes antes de iniciar la transacción, por lo que otro flujo podía abrir una cuenta después de esa lectura.
- **Corrección:** la mutación de estado inicia una transacción y bloquea la fila de mesa antes de comprobar cuentas pendientes; el envío D2 adquiere el mismo bloqueo antes de crear una cuenta individual.
- **Prevención:** comprobar invariantes bajo los mismos locks y probar ambas intercalaciones con conexiones independientes; un test secuencial no demuestra ausencia de carreras. Evidencia: `EnviosConcurrencyTests`.

### ERR-013 — Token retenía autorización tras cambiar el rol
- **Estado:** corregido y cubierto por regresión automatizada.
- **Contexto:** un token válido podía conservar el rol anterior aunque el usuario cambiara de rol o quedara inactivo, permitiendo reproducir una intención con autorización obsoleta.
- **Corrección:** cada solicitud autenticada comprueba que el usuario sigue activo y que su rol coincide con el claim; ante discrepancia se rechaza el token.
- **Prevención:** probar revocación antes de reproducir una clave idempotente; la clave no sustituye autorización vigente.

### ERR-014 — Catálogo demostrativo duplicado aceptado como idéntico
- **Estado:** corregido; prueba de regresión añadida.
- **Contexto y causa confirmada:** la comprobación comparaba número de filas y valores permitidos, pero no exigía que todos los nombres fueran distintos. Dos filas idénticas podían sustituir a una fila faltante y pasar como catálogo exacto.
- **Corrección:** consultar el catálogo sin reutilizar entidades rastreadas y exigir tantos nombres distintos como productos esperados antes de aceptar una segunda ejecución sin cambios; un catálogo distinto se rechaza sin modificaciones.
- **Prevención:** probar duplicados y ausencias simultáneas al verificar conjuntos, además de los valores individuales y el conteo.
- **Evidencia:** `DemoCatalogSeeder.cs` y `Duplicate_demo_name_with_missing_product_is_rejected_without_changes` en `PedidosPersistenceTests.cs`; prueba con base PostgreSQL temporal.

### ERR-015 — Respuesta sin campo de cuenta confundida con cuenta vacía
- **Estado:** corregido y cubierto por prueba Desktop.
- **Contexto y causa confirmada:** al deserializar `{}`, el valor predeterminado `null` podía confundirse con `{"pedido":null}`, aunque solo este último confirma que no hay cuenta activa.
- **Corrección:** comprobar que la respuesta JSON contiene explícitamente `pedido` antes de aceptarla; ante ausencia se rechaza como respuesta inválida y se mantienen los datos anteriores sin habilitar escritura.
- **Prevención:** distinguir campo ausente, valor nulo y respuesta válida en contratos donde el vacío tiene significado de negocio.
- **Evidencia:** `CuentaPedidoApiService.cs` y `Empty_account_is_a_valid_result` en `ProductosPedidoApiServiceTests.cs`.

### ERR-016 — Fallo inesperado de lectura podía dejar el envío sin bloqueo de incertidumbre
- **Estado:** corregido y cubierto por prueba con fallo inyectado; no se observó como incidente de usuario.
- **Contexto y causa confirmada por revisión:** el manejo inicial enumeraba solo ciertos tipos de excepción; una `IOException` al recibir la respuesta podía salir sin marcar la intención como incierta, aun cuando el servidor pudiera haber confirmado.
- **Corrección:** cualquier excepción no tratada tras iniciar el flujo de envío conserva la intención y bloquea acciones conflictivas; los rechazos HTTP conocidos de la primera solicitud se distinguen. La lectura de datos falla en modo conservador si recibe un error inesperado.
- **Prevención:** inyectar errores de transporte y lectura de cuerpo, no solo códigos HTTP o timeouts, y verificar ausencia de reintentos automáticos y conservación de la intención.
- **Evidencia:** `PedidosSalonViewModel.cs` y `Unexpected_response_io_failure_is_uncertain_and_cannot_create_new_intention` en pruebas Desktop.

### ERR-017 — Pantalla de pedidos sin datos actuales y botón deshabilitado poco distinguible
- **Estado:** diagnóstico mejorado y estilo ajustado; pendientes comprobación visual y causa concreta del fallo de consulta de la captura.
- **Contexto:** la captura de D4 muestra «Sin datos actuales», cuenta aún no consultada y un botón de envío aparentemente deshabilitado con fondo naranja y texto tenue. La captura no indica si falló la red, la autorización, la versión de API, el esquema o el contenido de la respuesta.
- **Causa confirmada del diagnóstico genérico:** el ViewModel agrupaba distintos errores HTTP bajo el mismo mensaje; la causa exacta en el equipo del usuario no está confirmada. **Hipótesis visual:** la prioridad del estilo primario podía conservar el fondo naranja sobre el estado deshabilitado.
- **Corrección:** diferenciar 401, 403, 404, 5xx y datos inválidos sin mostrar detalles internos; añadir estilo explícito para `Button.primario:disabled`.
- **Prevención:** probar estados de plantilla en ejecución y no diagnosticar esquema o conectividad solo por una captura; ofrecer una base temporal con catálogo ficticio para reproducir el flujo.
- **Evidencia:** captura aportada por el usuario; `PedidosSalonViewModelTests.Api_failures_explain_the_problem_without_enabling_writes`; `PedidosSalonView.axaml`. El ajuste visual todavía no ha sido confirmado en una nueva captura.

### ERR-018 — Aviso de EF Core por consultas con varias colecciones
- **Estado:** consulta identificada y estrategia existente declarada explícitamente; no se ha medido su rendimiento ni confirmado un fallo funcional.
- **Contexto:** `AcceptanceHost --pedidos-smoke` completó el recorrido local, pero la API emitió dos `MultipleCollectionIncludeWarning` de EF Core.
- **Causa:** la lectura de `CuentasService.GetActivaAsync` carga la jerarquía de comandas y detalles en una sola consulta. EF advierte de las colecciones anidadas, tanto para mesa como para grupo; no se ha medido su impacto.
- **Decisión:** mantener `AsSingleQuery()` explícito y la transacción `RepeatableRead`, sin prometer mejoras. Una consulta dividida añade viajes y debe evaluarse con datos representativos antes de sustituir la estrategia actual.
- **Evidencia:** diagnóstico de pila durante `--pedidos-smoke` con base temporal; no mide latencia ni rendimiento de producción.

### ERR-019 — Se indicó comprobar una transición de Cocina desde una vista aún no implementada
- **Estado:** aclarado y documentado; prevención aplicable a verificaciones futuras.
- **Contexto:** al revisar D4 se indicó al usuario que comprobara que una comanda alcanzara `Lista` desde Cocina. La especificación y `tasks.md` muestran que la pantalla Desktop de Cocina corresponde a D5 y todavía no está implementada. El usuario no tenía, por tanto, una vista disponible para realizar ese paso.
- **Causa confirmada:** se confundió la API de Cocina D3, que sí permite y prueba las transiciones, con la interfaz Desktop KDS planificada en D5. La prueba automatizada REST de API no equivale a una pantalla disponible ni a validación visual.
- **Corrección:** se aclaró que la captura de salón verifica cuenta/comandas abiertas, pero no la transición visual a `Lista`; se dejó dicha observación para la integración posterior con D5.
- **Prevención:** antes de pedir pruebas manuales, contrastar cada paso con `specs/003-pedidos-kds/tasks.md` y comprobar que exista una ruta/UI implementada para el rol y acción. Distinguir estado implementado en API, estado disponible en Desktop y escenario de integración futuro. No presentar una vista planificada como disponible.
- **Evidencia:** confirmación del usuario de que los puntos 1, 2, 3, 5, 6 y 7 de la lista D4 funcionan; captura de mesa 04 con dos comandas `Abierta`; `tasks.md` D5 deja KDS Desktop pendiente. La transición `Lista` está cubierta por smoke de API/REST, no observada en UI.

### ERR-020 — Polling de KDS seguía consultando durante la pausa de sesión
- **Estado:** corregido y cubierto por prueba automatizada.
- **Contexto y causa confirmada por revisión:** el ViewModel publicaba el estado de sesión pausada, pero el ciclo de polling seguía intentando solicitudes, que fallaban en el transporte en vez de esperar la reanudación.
- **Corrección:** Cocina ahora suspende el polling en estado `Paused`, lo reanuda al recibir transición a sesión activa y detiene su ciclo al cerrar/terminar sesión.
- **Prevención:** probar tanto que una mutación se bloquea en pausa como que no se emiten nuevas lecturas hasta reanudación; una etiqueta visual de pausa no demuestra que el trabajo de fondo esté suspendido.
- **Evidencia:** `CocinaViewModel.cs` y `Paused_session_blocks_mutation_and_recovers_only_after_read` en `CocinaViewModelTests.cs`.

### ERR-021 — API de pedidos apuntaba a una base sin esquema 003
- **Estado:** causa confirmada; migración pendiente de autorización y destino exacto.
- **Contexto:** el mapa responde por HTTPS como usuario `admin`/rol mostrado `Jefa`, pero Pedidos de salón no carga catálogo/cuenta. El log de API identifica `Npgsql.PostgresException 42P01`, relación `producto` inexistente, desde `ProductosService.GetPageAsync`.
- **Causa confirmada:** el proceso API está conectado a una base cuyo esquema no contiene la tabla creada por la migración D1 de 003. La API no aplica migraciones al arrancar por diseño. HTTPS y el rol no crean el esquema. No se ha determinado el nombre de la base ni si contiene datos que deban preservarse.
- **Corrección:** todavía no se aplicó ninguna migración ni se cargó catálogo. Primero confirmar nombre exacto de base, entorno y que sea destino autorizado. Después respaldar si contiene datos valiosos, aplicar `dotnet ef database update` con la configuración de esa API, y preparar catálogo ficticio solo mediante el comando protegido en Development y bases con prefijo permitido `elcarbonazo_demo_*` o `elcarbonazo_test_*`. No ejecutar seed en bases ajenas/no elegibles.
- **Prevención:** probar conexión API contra esquema vigente antes de declarar catálogo operativo; no añadir migración automática en startup. Verificar entorno/base efectivos, migrar con rol autorizado y realizar seed explícito e idempotente únicamente en destino aislado permitido.
- **Evidencia:** log aportado por el usuario (SQLSTATE 42P01, tabla `producto` ausente); capturas de mapa conectado y vista de pedidos sin catálogo. No imprimir ni registrar la cadena de conexión.

### ERR-004 — Carrera cuando refresh termina inmediatamente
- **Estado:** corregido y cubierto por regresión automatizada.
- **Contexto y síntoma:** una prueba de refresh rechazado produjo `NullReferenceException` en vez de informar sesión expirada.
- **Causa:** una tarea de refresh podía completarse sincrónicamente y liberar el campo compartido antes de que el consumidor guardara una referencia local.
- **Corrección:** se conserva la tarea en una variable local antes de iniciar la limpieza del campo compartido.
- **Prevención:** probar tareas que completan tanto sincrónicamente como de forma diferida, además de consumidores concurrentes, logout y expiración. No asumir que un método asíncrono siempre cede el control.
- **Evidencia:** `Services/SessionManager.cs`; `FailedRefreshThrowsInsteadOfReturningDisposedResponse` y `ConcurrentHumanInteractionsShareOneRefresh` en pruebas Desktop.

### ERR-005 — GET y mutación podían estar simultáneamente en curso
- **Estado:** corregido y cubierto por pruebas focalizadas.
- **Contexto y síntoma:** se descartaba la respuesta de una lectura antigua, pero podía iniciarse una escritura antes de que el GET anterior terminara.
- **Causa:** invalidar una generación evita aplicar una respuesta obsoleta, pero no cancela ni espera por sí mismo la solicitud física.
- **Corrección:** el coordinador reserva la mutación, invalida la lectura y espera a que termine antes de enviar; si falla la lectura o cambia la sesión durante la espera, no envía el cambio.
- **Prevención:** distinguir consistencia de resultados y ausencia de solapamiento; probar GET lento, fallo, cambio de sesión y cancelación, sin perder el clic ni comparar versiones como relojes.
- **Evidencia:** `Services/MesasRequestCoordinator.cs`; `OldReadFinishesBeforeMutationAndCannotApply` y `MutationWaitsForActivePollingWithoutApplyingItsStaleResult`.

### ERR-006 — Plazo de lectura no incluía deserialización
- **Estado:** corregido para la lectura de mesas y probado con una lectura bloqueada.
- **Contexto y síntoma:** el timeout del transporte cubría la recepción del cuerpo, pero su alcance terminaba antes del procesamiento JSON en el servicio.
- **Corrección:** el GET de mesas usa un token con plazo que abarca solicitud y deserialización; se verifica la cancelación después de procesar.
- **Prevención:** definir el alcance completo del timeout y probar envío, cuerpo y procesamiento. Reconocer que la cancelación es cooperativa y no prometer una interrupción inmediata de trabajo que no observe el token.
- **Evidencia:** `Services/MesasApiService.cs`; `ReadDeadlineCoversJsonBodyProcessing`.

### ERR-007 — Un rechazo de cierre de grupo parecía éxito
- **Estado:** corregido y cubierto por prueba.
- **Contexto y síntoma:** ante una cuenta pendiente, el mensaje de rechazo podía ser sustituido por «Grupo cerrado».
- **Causa:** el código de aplicación de resultado trataba el cierre como exitoso sin verificar que la operación hubiera terminado correctamente.
- **Corrección:** se aplica el mensaje de éxito únicamente cuando la operación realmente fue exitosa; el rechazo mantiene su mensaje y solicita reconciliación.
- **Prevención:** probar explícitamente éxito, 4xx, conflicto, 5xx, timeout y fallo de transporte; no ejecutar lógica de éxito desde `finally` sin una señal de resultado válido.
- **Evidencia:** `ViewModels/MesasViewModel.Grupos.cs`; `GroupFailureIsNotRetriedAndRequiresReconciliation`.

## Ejecución, validación y medición

### ERR-008 — API y Desktop apuntaban a puertos distintos
- **Estado:** causa identificada y ejecución corregida con el perfil adecuado.
- **Contexto y síntoma:** el login informó que no podía conectar con la API. La API se había iniciado con el perfil HTTP por defecto mientras Desktop apuntaba al perfil HTTPS local.
- **Corrección:** iniciar la API con el perfil que coincida con la configuración utilizada por Desktop; no desactivar TLS para ocultar el problema.
- **Prevención:** al dar instrucciones de ejecución, comprobar `launchSettings.json`, dirección configurada y entorno; distinguir red, credenciales, configuración y errores TLS. Usar overrides de Development solo en pruebas locales.
- **Evidencia:** `src/ElCarbonazo.Api/Properties/launchSettings.json`, `Services/DesktopConfigurationService.cs` y la comprobación de conexión realizada durante la conversación.

### ERR-009 — Confundir duración del PUT con convergencia visual
- **Estado:** aclarado y documentado; medición cronometrada de extremo a extremo pendiente para producción.
- **Contexto y síntoma:** los 47 valores mostrados por el anfitrión correspondían a la duración de PUT, no al tiempo que la tarjeta tardaba en renderizarse.
- **Causa:** el instrumento solo marcaba la confirmación HTTP; no registraba el instante de presentación en la pantalla observadora.
- **Corrección:** se documentaron por separado los tiempos de PUT, la cadencia local medida y la estimación visual del usuario de 1–1,5 s, junto con sus límites.
- **Prevención:** describir exactamente inicio, fin, reloj, entorno y resolución de cada medición. No convertir una estimación visual o un tiempo de red en una garantía de render ni en un SLA; esperar a observar cada muestra cuando se evalúen estados sucesivos.
- **Evidencia:** `docs/validacion-t5.md` y el modo `--cadencia`/`--medir` del anfitrión de aceptación.

### ERR-010 — Herramienta temporal de medición terminó ante 401
- **Estado:** corregido en la herramienta; no implica un cambio de reglas de autenticación de la aplicación.
- **Contexto y síntoma:** durante la medición, una respuesta 401 provocó una excepción no controlada y cerró el anfitrión temporal.
- **Causa:** se asumía que la sesión de la herramienta permanecería autorizada durante toda la medición. El 401 observado no identifica por sí solo si la sesión había expirado o sido revocada.
- **Corrección:** la herramienta informa el código de autenticación y solicita una nueva acción humana para iniciar sesión; no repite automáticamente una escritura. Los 5xx y fallos de transporte tras enviar se informan como resultado potencialmente incierto.
- **Prevención:** probar expiración, revocación, 401/403, errores de servidor y cancelación también en herramientas auxiliares. Mantener aislada la base temporal y no mostrar tokens en consola.
- **Evidencia:** `tests/ElCarbonazo.AcceptanceHost/Program.cs`; el usuario confirmó que el modo de medición funcionó después del ajuste.

## Revisión al cerrar una tarea

### ERR-011 — Barra de ventana clara y maximizar sin confirmar
- **Actualización:** el usuario confirmó que el botón central no responde con clic real, aunque el doble clic en la barra sí funciona. La prueba previa con mensajes de ventana no representaba esa interacción. Se reemplazó el botón de SukiUI por uno propio, evitando su interceptación especial de hit test para Snap Layout; el menú Snap Layout al pasar el puntero no está disponible en el reemplazo. La causa exacta en el equipo del usuario y la corrección con clic físico siguen pendientes de confirmación.
- **Corrección posterior:** el reemplazo inicialmente carecía del rol `User` de decoración de ventana; SukiUI podía interpretar la pulsación como arrastre de la barra. Se asignó dicho rol y se verificó en una ventana de prueba mediante eventos de ratón del sistema que el botón maximiza y restaura. Falta la confirmación del usuario en su configuración.
- **Estado:** ajustado; captura de la ventana propia y prueba local de maximizar/restaurar realizadas, pendiente revisión del usuario en su configuración y escalado.
- **Contexto y síntoma:** el usuario observó una barra blanca demasiado ancha y reportó que maximizar/restaurar no funcionaba. Una primera configuración de tema oscuro seguía mostrando una barra clara con iconos de bajo contraste.
- **Causa:** la barra usa la plantilla y el fondo de `GlassCard` de SukiUI; cambiar solo el tema de la ventana no garantizó un fondo oscuro. No se ha establecido una causa única del fallo original de maximizar.
- **Corrección:** adaptar el fondo de la barra mediante el recurso y plantilla instalados, reducir el tamaño de sus iconos y mantener las decoraciones y capacidades de ventana activas. Se comprobó localmente una transición a maximizada y de vuelta a normal en una ventana de prueba.
- **Prevención:** revisar la plantilla de la versión realmente instalada, comprobar contraste y tamaño en una captura de la ventana propia, y probar los controles de ventana en ejecución. No afirmar compatibilidad con otros equipos o escalas sin probarlos.
- **Evidencia:** `src/ElCarbonazo.Desktop/MainWindow.axaml`; la comprobación local no sustituye la aceptación visual del usuario.

- ¿Se consultaron la especificación y las entradas relacionadas antes de editar?
- ¿Se agregó o ejecutó una prueba que reproduce el fallo cuando es viable?
- ¿Se verificó el comportamiento real cuando una prueba automatizada no cubre la interfaz o el entorno?
- ¿Se conservaron los datos y cambios ajenos, usando bases temporales para pruebas destructivas?
- ¿La respuesta distingue claramente implementado, probado, observado por el usuario y pendiente?
- ¿Se debe registrar una nueva lección, indicando evidencia y sin exponer secretos?
