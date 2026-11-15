# Especificación 004 — Caja, cobros y comprobante interno

**Estado:** aceptado para demostración universitaria local aislada según `tasks.md` y `docs/validacion-004.md`. No es aprobación fiscal, certificación de seguridad ni autorización para operar con dinero real.

## 1. Objetivo y contexto

Agregar a la aplicación una caja compartida para apertura/cierre de turno, consulta de cuentas individuales y de grupo, cobro completo y consulta de comprobante interno. La API es autoridad de permisos, elegibilidad, importes, concurrencia, recuperación y auditoría; Desktop accede exclusivamente por REST; PostgreSQL conserva resultados durables. La aplicación no depende de Internet.

El resultado debe ser pulido y demostrable ante el restaurante y el profesor, con límites explícitos. Recuperación durable y transacciones correctas son requisitos de diseño, pero no bastan para afirmar idoneidad para cobros reales.

## 2. Alcance

### Incluido

- Una caja compartida: como máximo un turno abierto.
- Apertura con fondo inicial no negativo, incluido C$0.00 con confirmación.
- Roles `Jefa` y `Encargado de Caja` para consultar caja, abrir, cobrar, recuperar y cerrar; los actos conservan el actor efectivo.
- Listado paginado de cuentas por mesa y grupo, incluidas las no elegibles con causa visible.
- Cobro de cuenta completa con un único método (efectivo, tarjeta o transferencia), exclusivamente en córdobas.
- Verificación humana externa previa para tarjeta/transferencia y referencia obligatoria.
- Vuelto calculado por API; sin pagos parciales o mixtos.
- Comprobante interno histórico marcado `DEMOSTRACIÓN`, consultable en pantalla.
- Cierre con conteo total único de efectivo, totales por método y motivo obligatorio ante sobrante/faltante.
- Persistencia durable y recuperación explícita de aperturas, cobros y cierres con resultado incierto.
- Auditoría transaccional, restricciones PostgreSQL y pruebas de carreras/fallos.

### Fuera de alcance

Egresos, retiros y gastos; pago parcial/mixto; anulaciones, reembolsos, edición o corrección de cobros confirmados; inventario; propinas; conversión USD; cálculo o declaración fiscal; factura fiscal/electrónica; impresión térmica/PDF; integración o validación bancaria; almacenamiento de PAN, CVV, credenciales o datos del cliente; operación offline con escrituras en cola; preparación para producción.

## 3. Términos y fuente de verdad

- **Cuenta:** entidad `pedido` de 003; tiene exactamente un destino (`id_mesa` o `id_grupo`) y comandas asociadas. No crear una cuenta nueva para cobrar.
- **Cuenta elegible:** cuenta activa con detalle verificable, total positivo y todas sus comandas en `Lista` al momento de ejecutar el cobro.
- **Turno:** período de caja compartida desde apertura confirmada hasta cierre confirmado. No pertenece a la sesión ni al dispositivo del abridor.
- **Cobro:** liquidación única y completa de una cuenta elegible, confirmada en transacción junto con el estado `Pagado`, comprobante, operación durable y auditoría.
- **Resultado incierto:** Desktop no puede determinar si una escritura alcanzó resultado terminal. Ni un GET ordinario ni el paso del tiempo lo resuelven.
- **DEMOSTRACIÓN:** marca obligatoria del comprobante; no significa que el documento tenga validez fiscal.

La API calcula y vuelve a validar todo. El precio/nombre histórico de líneas de 003 no se reemplaza con catálogo actual. Los importes se manejan como decimal exacto a dos posiciones, nunca `float`/`double`.

## 4. Reglas funcionales

### RF-01 — Autorización y trazabilidad

1. La API valida autenticación, vigencia del usuario y rol en cada solicitud. Solo `Jefa` y `Encargado de Caja` pueden operar este módulo.
2. El usuario que abre, cada actor que registra un cobro y quien cierra se guardan por separado. Jefa y Encargado pueden actuar sobre el turno abierto por la otra persona.
3. Logout, expiración o cambio de terminal del abridor no cierra ni bloquea el turno. UUID de instalación es metadato de auditoría, nunca autorización.
4. Un resultado recuperable solo se consulta por su propietario, Jefa o Encargado de Caja autorizado según la matriz de este módulo; supervisar no equivale a ejecutar como el propietario. La intervención, cancelación y actor supervisor se auditan.

### RF-02 — Turno compartido

1. Puede existir como máximo un turno `Abierto` en todo el sistema; PostgreSQL lo garantiza además de validaciones/locks de API.
2. Abrir requiere fondo inicial `>= C$0.00`, confirmación explícita y actor autorizado. Cero es válido.
3. Turno cerrado es inmutable e irreabrible. Se puede abrir un turno posterior con identidad nueva; no se corrige el anterior mediante edición.
4. No se abre ni cierra turno si existe una operación de apertura/cierre/cobro relevante sin resultado terminal que pueda afectar esa caja. Un cobro pendiente impide cerrar.
5. El turno puede cerrarse aunque queden cuentas sin pagar. Desktop presenta el conteo y la advertencia; las cuentas siguen pendientes y no se imputan como pagadas. Para revisar sus estados se reutiliza el listado paginado de cuentas de Caja, que también incluye cuentas históricas; no se exige otro listado dentro del resumen de turno.

### RF-03 — Consulta y elegibilidad de cuenta

1. Caja lista cuentas individuales y compartidas de grupo. Las no cobrables siguen visibles con estado y motivo; no se ocultan para aparentar que no existen.
2. Solo es elegible una cuenta activa `Listo`, con una o más comandas y todas `Lista`, detalle histórico íntegro, importes válidos y `total > 0`.
3. Cuenta con comandas `Abierta`/`En Preparación` se muestra bloqueada indicando los estados pendientes. Cuenta heredada activa sin comandas, cuenta sin detalle, de total cero, inconsistente o anómala se bloquea para revisión y no admite cobro ficticio.
4. Una cuenta ya pagada no se vuelve a cobrar. `Cancelado` y cualquier estado no contemplado se muestra como no cobrable; no se cambia por esta pantalla.
5. El GET es informativo. Al preparar/ejecutar una intención, la API revalida elegibilidad, destino, estado, comandas, líneas, total y turno bajo los locks/transacción definidos en `plan.md`.
6. La solicitud fija el total y huella de líneas que la persona revisó y confirmó. Si importe, composición, estado, destino o versión observable cambia antes de ejecutar, se rechaza con conflicto de revisión, no se cobra otro total automáticamente; Desktop refresca y requiere nueva confirmación humana con nuevo UUID.

### RF-04 — Métodos y registro de cobro

1. Cada cuenta se paga completa con exactamente uno de `Efectivo`, `Tarjeta` o `Transferencia`; no se admite saldo remanente ni combinación.
2. Efectivo: recibido `>= total`; API calcula `vuelto = recibido - total`. Si recibido es menor, no persiste pago ni operación de cobro confirmada. Vuelto no negativo.
3. Tarjeta/transferencia: el actor declara que verificó aprobación/recepción fuera de El Carbonazo e introduce referencia obligatoria, no vacía después de normalizar espacios exteriores, limitada por el plan técnico. El sistema no realiza autorización bancaria.
4. Una referencia coincidente con otro cobro, del mismo método y tras NFC + trim exterior (comparación exacta sensible a mayúsculas), genera advertencia sin revelar la referencia ni datos de otro comprobante. Sin confirmación explícita la API rechaza esa intención como `REFERENCIA_REQUIERE_CONFIRMACION`; el operador revisa la advertencia y crea una nueva intención con confirmación expresa. La confirmación y actor quedan vinculados al cobro. No se afirma que coincidir sea fraude o duplicado bancario.
5. No recopilar datos completos de tarjeta, CVV, PIN, credenciales bancarias ni datos del cliente. La referencia puede contener información operacional; debe limitarse, sanearse y no aparecer en logs técnicos.
6. Cobro, actualización del `pedido` a `Pagado`, snapshot del comprobante, resultado durable y evento de auditoría se confirman en una única transacción. Si cualquiera falla, ninguno queda confirmado.
7. No hay endpoint de anulación, edición, reembolso o borrado. Cobro confirmado incorrectamente queda para revisión humana/procedimiento futuro; la UI informa que no hay corrección automática.
8. Si un adquirente/terminal externa aprueba y el registro local no confirma, advertir que el pago externo pudo ocurrir. No volver a cobrar ni registrar otra intención automáticamente; revisión humana requerida.

### RF-05 — Importes y reglas fiscales de demostración

1. Moneda única NIO/córdobas, escala 2. No hay conversión USD, propina ni egresos.
2. Para las cuentas de 003, conservar `impuestos = 0` y `total = subtotal` conforme a información del negocio comunicada para la demostración. No declarar que constituye determinación fiscal vigente, asesoría legal o cumplimiento.
3. El cobro usa importes confirmados e históricos de `pedido`/sus líneas; nunca recalcula con el catálogo actual ni acepta precio/total autoritativo del cliente.
4. API valida rangos, precisión y overflow; montos usan `NUMERIC(12,2)` con tope C$9,999,999,999.99, y total cobrable debe ser positivo. Efectivo recibido está en el mismo rango y escala.

### RF-06 — Efecto sobre mesa y grupo

1. Pagar no libera automáticamente una mesa ni cierra un grupo.
2. Mesa individual pagada permanece ocupada hasta liberación explícita autorizada conforme a 001.
3. Grupo pagado permanece activo y sus mesas `Agrupada` hasta cierre explícito autorizado. Mientras el grupo pagado continúe activo, no se abre otra cuenta para él.
4. Esto es un cambio explícito respecto de la regla de envío de 003 que admite nuevos envíos mientras un grupo activo conserve cuenta `Pagado` previa. 004 debe actualizar la regla autoritativa de 003 mediante cambio documentado/compatibilidad: bloquear nuevos envíos a grupo activo con cuenta pagada hasta cierre del grupo; no alterar registros históricos. Se requiere prueba cruzada y aceptación antes de implementar. No se modifica silenciosamente el comportamiento de 003.
5. Para mesa individual, la regla vigente requiere liberar explícitamente antes de abrir otra cuenta. El pago por sí solo no habilita un envío nuevo.

### RF-07 — Cierre y conteo

1. Solo hay un cierre por turno. Requiere conteo físico como un único total no negativo en córdobas y confirmación.
2. Sin egresos: `efectivoEsperado = fondoInicial + suma(efectivoRecibido - vuelto)` de cobros de efectivo confirmados en ese turno. Tarjeta y transferencia se informan separadamente; no forman parte del efectivo contado.
3. `diferencia = efectivoContado - efectivoEsperado`; mostrar explícitamente sobrante, faltante o coincidencia, además del importe.
4. Diferencia distinta de cero exige motivo no vacío y confirmación. Jefa o Encargado autorizado pueden confirmar; se conserva actor, motivo acotado, conteo y fotografía de totales.
5. Cerrar con cuentas impagas es válido con advertencia y recuento. Para consultar su detalle se utiliza el listado paginado de cuentas existente, no una segunda lista en el resumen. No cerrar con intención relevante no terminal ni con resultado de cobro incierto.
6. El resumen no afirma conciliación de egresos (no soportados), conciliación bancaria ni cierre fiscal.

### RF-08 — Comprobante interno

1. Todo cobro confirmado genera un identificador/número interno único y snapshot histórico, identificable como `COMPROBANTE INTERNO — DEMOSTRACIÓN — NO ES FACTURA FISCAL`.
2. Contiene como mínimo número, fecha UTC de registro (presentada en `America/Managua`), destino mesa/grupo, referencia de cuenta, líneas (nombre/cantidad/precio/subtotal históricos), subtotal, impuestos cero, total, método, actor que cobró y datos de efectivo (recibido/vuelto) o referencia externa. Sin datos del cliente.
3. Solo Jefa y Encargado de Caja consultan el detalle/historial paginado. Aplicar autorización también en API. La consulta no modifica ni audita por cada lectura.
4. Snapshot no cambia si luego cambian catálogo, usuarios visibles, reglas o traducciones. Los registros no se borran por esta pantalla.
5. Impresión y PDF quedan fuera. Un fallo futuro de impresión no debe revertir ni repetir el cobro.

### RF-09 — Operaciones recuperables

1. Apertura, cobro y cierre usan UUID de operación generado una sola vez por Desktop y protocolo durable prepare/execute/query/cancel, inspirado en los principios de 002 pero separado de su implementación pospuesta de operaciones de mesa.
2. Preparar registra actor, tipo, versión de contrato y argumentos validados e inmutables, sin cambiar saldo, cuenta ni turno. No se envían datos de tarjeta ni secretos. Efectivo insuficiente puede terminar como rechazo durable sin crear cobro ni marcar pedido Pagado.
3. Ejecutar solo consume la intención preparada; misma operación ejecutada más de una vez no duplica efecto y reproduce resultado terminal. Mismo UUID con distinto actor/tipo/argumentos es conflicto sin filtrar datos ajenos.
4. Estados terminales mínimos: `Completada`, `Rechazada`, `Cancelada`; preparada/no encontrada no demuestra que una ejecución retardada no llegue. El sistema nunca presenta `Preparada` como resultado seguro.
5. Ante timeout, desconexión, 5xx o cuerpo inválido tras escritura, Desktop informa incertidumbre, no reintenta automáticamente, retiene el UUID local, bloquea acciones conflictivas y recupera ese mismo UUID explícitamente. Un GET de caja/cuenta no es prueba de terminación.
6. El propietario puede consultar y resolver su operación. Jefa puede consultar/cancelar operación ajena y establecer barrera durable, pero nunca ejecutarla como propietario. Encargado de Caja no propietario no consulta ni resuelve operaciones ajenas. Supervisor no recibe permisos de caja por ser supervisor.
7. Cancelación compite serializadamente con ejecución. Si gana, ejecución tardía no produce efecto; si ejecución completó, cancelación informa el resultado real y no lo revierte. Toda cancelación supervisada requiere confirmación y auditoría.
8. Una operación relevante sin resultado terminal bloquea reintento equivalente, segundo cobro de la cuenta, cierre afectado y cualquier transición que pueda invalidar los argumentos. No desbloquear por tiempo, GET ni logout.
9. Si la aprobación de terminal externa ocurrió pero el servidor no confirma el registro, recovery no autoriza reintentar pago externo; requiere investigación humana y procedimiento futuro. Resultado externo no verificable no se convierte automáticamente en cobro interno confirmado.
10. Desktop conserva una sola intención local no terminal en `%ProgramData%\ElCarbonazo\recovery\caja-journal.json`, con ACL de instalación, escritura atómica, identidad de instalación y versión de formato. No contiene token, contraseña, referencia ni body. Archivo ausente/corrupto, UUID de instalación discordante o fallo de persistencia bloquea nuevas mutaciones recuperables; no se repara creando un archivo vacío.
    Para la demostración local aislada, el usuario autorizó usar únicamente la cuenta Windows actual como identidad operativa. No se exige crear ni utilizar una segunda cuenta Windows real para aceptar esta fase; se conservan las comprobaciones de ACL y rechazo de permisos amplios mediante pruebas en archivos temporales. El comportamiento efectivo desde otra cuenta no se considera validado y su evaluación queda para una eventual preparación de uso real.
11. Resultados terminales se conservan completos al menos cinco años y luego solo se compactan mediante tarea administrativa explícita que mantiene tombstone permanente contra reuso. Nunca purgar operaciones no terminales.

## 5. Matriz de permisos

| Operación | Jefa | Encargado de Caja | Supervisor | Mesero | Cocina |
|---|---:|---:|---:|---:|---:|
| Consultar caja/cuentas cobrables y bloqueadas | Sí | Sí | No | No | No |
| Abrir turno | Sí | Sí | No | No | No |
| Cobrar / consultar comprobantes | Sí | Sí | No | No | No |
| Cerrar turno | Sí | Sí | No | No | No |
| Resolver intención propia | Sí | Sí | No | No | No |
| Supervisar/cancelar intención ajena | Sí | No | No | No | No |
| Liberar mesa/cerrar grupo | Según permisos de 001 | Según permisos de 001 | Según 001 | Según 001 | No |

La UI no es frontera de autorización. Cualquier expansión requiere decisión, cambio de matriz, contrato y prueba de regresión.

## 6. UX y presentación

- Destino visual coherente con «Carbón & Fuego», legible, táctil y operable con teclado/foco.
- Separar claramente resumen de turno, cuentas cobrables y bloqueadas, captura de pago y comprobante.
- Mensajes textuales explican elegibilidad, monto confirmado, vuelto, referencia repetida, diferencia y resultado incierto; color nunca es el único indicador.
- Confirmaciones muestran monto/destino/método exactos antes del paso irreversible. Tarjeta/transferencia incluyen afirmación explícita de verificación externa.
- Estados de red/sesión/sin datos/desactualizado/incertidumbre no habilitan mutaciones. No indicar “pagado” antes de commit confirmado.
- Estados bloqueados incluyen causa y acción válida siguiente. Navegación solo a destinos implementados.
- Revisión práctica local de la interfaz y registro honesto de sus límites; compilación por sí sola no acepta la interfaz. Las mediciones completas de foco, tamaño mínimo y contraste no se atribuyen a la revisión comunicada.

## 7. Fuera de alcance y aptitud real

Esta especificación no habilita uso con dinero real. Antes de operación real se requiere, como mínimo, validación con negocio/asesoría competente de impuestos, comprobantes y obligaciones; evaluación de adquirentes y conciliación; procedimientos autorizados de anulación/reembolso/errores; privacidad y retención; roles y segregación; instalación TLS/LAN, permisos PostgreSQL, backup/restauración, monitoreo, continuidad y pruebas físicas; aceptación del restaurante. No presentar el comprobante de demostración como factura.

## 8. Criterios de aceptación documental y funcional

**Perfil universitario acordado:** se conservan las reglas y protecciones siguientes y se usan las pruebas esenciales existentes o focalizadas para verificarlas. La matriz exhaustiva de intercalaciones y puntos de fallo, y el instalador empaquetado, quedan para una etapa posterior; no se consideran probados ni requisitos para cerrar esta demostración local. La revisión práctica y conformidad del usuario están registradas en `docs/validacion-004.md`.

1. No se crea segundo turno abierto bajo concurrencia; logout del abridor no altera el turno.
2. Cero inicial requiere confirmación; turno cerrado no se edita ni reabre.
3. Las cuentas incompletas, heredadas, cero/anómalas aparecen bloqueadas con motivo; elegibilidad solo con todas las comandas `Lista`.
4. La cuenta se cobra una vez y por total exacto; efectivo insuficiente no deja cobro; vuelto, métodos y referencia quedan correctos.
5. Cambio de total/detalle/estado entre lectura y ejecución produce conflicto y nueva revisión humana.
6. Pago y `pedido=Pagado`, comprobante, auditoría y resultado durable son atómicos; fallo no deja parcialidad.
7. Una referencia repetida requiere acción explícita y la decisión se audita sin declarar duplicado fraudulento.
8. Pago no libera mesa ni grupo; grupo pagado no acepta cuenta nueva hasta cierre explícito conforme al cambio documentado de 003.
9. Efectivo esperado, diferencia, motivo y conteo se verifican; cuentas pendientes permiten cierre con aviso, operación incierta no.
10. Comprobante permanece fiel al momento del cobro, no contiene datos de cliente y lleva leyenda DEMOSTRACIÓN/no fiscal.
11. Preparación/ejecución/cancelación compiten de manera durable y segura ante concurrencia, timeout, caída y reinicio; no hay reintentos automáticos.
12. Migración es aditiva/no destructiva y probada exclusivamente en PostgreSQL temporal; se conservan registros 001/003.
13. Suites de API/Desktop y revisión visual cubren aceptación del perfil universitario; los límites de operación real se conservan documentados.

## 9. Decisiones cerradas / control de cambios

El usuario aprobó en conversación el alcance, reglas de negocio, actores, métodos, cierre, límites del comprobante y exigencia de recuperación durable que constan aquí. El usuario aprueba `plan.md` como línea base técnica para iniciar C1. El cambio contractual respecto de 003 está registrado como adenda explícita en la especificación de ese módulo: se exige cerrar el grupo pagado antes de aceptar nuevos pedidos. No se reescribe la historia ni el estado de cuentas existentes; la implementación debe actualizar reglas autoritativas y pruebas cruzadas. El usuario aprobó reutilizar el listado paginado de cuentas existente para revisar pendientes, sin añadir otro listado al resumen de turno; se conservan el conteo, la advertencia y el bloqueo por operación incierta. También aprobó aplazar la matriz exhaustiva y el instalador empaquetado para el perfil universitario, manteniendo las protecciones y pruebas esenciales. Ninguna decisión documental autoriza migrar otra base persistente. Cambios posteriores requieren actualizar spec/plan/tasks y aprobar antes de implementar.
