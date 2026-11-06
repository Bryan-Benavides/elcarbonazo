# Especificación 003 — Pedidos y monitor de cocina (KDS)

**Estado: preparación documental; no implementado.** El usuario aprobó dirección general, alcance, matriz de roles, semántica de estados y política fiscal descritas aquí. El contrato técnico de idempotencia, límites y HTTP queda como línea base propuesta para revisión antes de codificar; las decisiones de negocio no confirmadas siguen pendientes en §10. No altera las garantías vigentes de 001 ni convierte el MVP universitario en un sistema apto para operación real.

## 1. Objetivo y contexto

Digitalizar la toma de consumos para una mesa individual o una cuenta compartida de grupo y comunicar envíos a cocina mediante una cola local. Desktop usa Avalonia/MVVM y exclusivamente REST; la API ASP.NET Core contiene reglas, persistencia EF Core/PostgreSQL, permisos y auditoría. Operación en LAN, sin dependencia de Internet y sin desactivar TLS de producción.

`001-auth-mesas-mvp` ya contiene sesiones, roles, mesas, grupos y una entidad `pedido` parcial. Dispone de XOR de destino, claves restrictivas, importes y unicidad parcial de cuenta activa; **no existe todavía una toma funcional de pedidos ni KDS**. `002-operaciones-seguras` permanece pospuesto y trata operaciones de mesas/grupos, no resuelve automáticamente pedidos. No renumerar ni sobrescribirlo.

## 2. Alcance acordado

- Consultar un catálogo de demostración claramente identificado, organizado en Comida, Bebida y Licor, con disponibilidad y precio en NIO. Los datos de demostración se preparan explícitamente en un entorno aislado; no se insertan ni actualizan silenciosamente en producción.
- Preparar un borrador local con productos disponibles, cantidades y notas de hasta 150 caracteres; retirar o modificar líneas antes de enviarlas. No existe borrador compartido ni persistencia automática tras cerrar la vista o sesión, salvo decisión posterior.
- Enviar a cocina una o varias **comandas** para una sola cuenta activa por mesa o grupo. Cada envío posterior es una comanda nueva, no una modificación silenciosa de una enviada. Una comanda contiene líneas confirmadas y su propio estado de preparación.
- Mostrar en cocina la cola de comandas pendientes, destino, hora, productos, cantidades y notas; permitir transiciones autorizadas a preparación y listo. Mostrar en salón el estado de sus comandas y un aviso persistente visible de las listas mientras la cuenta esté abierta; no prometer notificación push, sonido o entrega instantánea.
- Conservar nombre y precio aplicados en las líneas confirmadas aunque cambie el catálogo. Calcular importes en la API; no confiar en precios ni totales enviados por Desktop.
- Para el régimen del negocio informado por el usuario (cuota fija), 003 no añade ni desglosa el IVA general del 15 %: los precios mostrados son los importes cobrados por los artículos, `impuestos = 0` y `total = subtotal`. Esto define el cálculo del prototipo según información del negocio; no es una determinación legal independiente ni convierte el pedido en factura o comprobante fiscal.
- Integrar 003 con la sesión y mapa existentes, sin crear rutas o botones que aparenten funcionar antes de implementar su destino.

## 3. Fuera de alcance explícito

- Inventario, recetas, comprobación de ingredientes, reservas de stock y su reversión. `disponible` es un atributo del catálogo, **no** prueba existencia de ingredientes. No devolver `Ingrediente Agotado` ni indicar validación de stock si no existe.
- Anulación o edición de líneas enviadas, PIN administrativo, excepciones de autorización y alertas de anulación. Se rechazan también en API; no basta ocultar botones. La integración futura deberá definir identidad, autorización, auditoría y, solo cuando exista inventario, reversión transaccional.
- Cobro, facturación, marcar pagado, propina, conversión de moneda y cerrar cuentas mediante un pago ficticio. Cuentas `Listo` continúan pendientes; no existe liberación automática de mesas o grupos. Pruebas y demostraciones de cierre usan únicamente bases temporales aisladas con preparación y limpieza controladas, no datos operativos ni endpoints de prueba públicos.
- Administración real de menú o empleados, notificaciones externas, impresión, operación offline con cola de escrituras y preparación para producción. No existen pedidos modificados después de enviados; los envíos nuevos se muestran como comandas nuevas, no como edición parpadeante de una anterior.

## 4. Reglas funcionales

### RF-01. Catálogo y borrador
1. Una consulta autorizada devuelve solo productos habilitados para seleccionar; la API vuelve a comprobar disponibilidad al confirmar. Categoría, precio y nombre provienen del servidor.
2. El borrador permite añadir varias líneas, modificar cantidad o notas y retirarlas antes del envío. Cantidades enteras positivas y notas máximo 150 caracteres Unicode; límites operativos propuestos en `plan.md §2.4` requieren aprobación antes de implementación. Rechazar artículos ausentes o no disponibles en vez de sustituirlos silenciosamente.
3. Si el precio cambia entre consulta y envío, la propuesta exige conflicto con información suficiente para que el usuario revise y confirme un nuevo borrador; no se factura un precio diferente sin informar. La forma exacta de versión de catálogo y la respuesta requieren aprobación técnica.

### RF-02. Cuenta y envío
1. Una cuenta tiene exactamente uno de `id_mesa` o `id_grupo`; como máximo una cuenta activa por destino. Las cuentas de grupo son compartidas y no se duplican por cada mesa miembro.
2. La confirmación del **primer envío**, no la creación de un borrador local, abre la cuenta y crea la primera comanda de manera atómica. Para mesa individual libre o reservada la misma transacción la cambia a ocupada; para grupo activo sus mesas conservan `Agrupada`. No se abre cuenta vacía desde este flujo.
3. El envío posterior agrega una comanda a la cuenta activa, nunca una segunda cuenta. Versiones y locks evitan perder envíos concurrentes y obligan a revisar conflictos, sin interpretar `xmin` como reloj.
4. La operación valida destino, grupo activo, permisos, productos, cantidades, versiones, importes y límites dentro de la transacción. Pedido, comanda, líneas, registro idempotente, cambios de mesa y auditoría se confirman juntos o ninguno se confirma.
5. La respuesta exitosa devuelve identificadores y datos confirmados, importes y versiones actuales. Errores se expresan como `ProblemDetails` con códigos estables; nunca exponer secretos ni excepciones internas.
6. Cada intento lógico de envío lleva `envioId` UUID generado una sola vez por Desktop y reutilizado únicamente para repetir exactamente la misma intención. La API persiste `envioId`, emisor, hash canónico del body y resultado confirmado en la misma transacción que cuenta/comanda/líneas/auditoría. Una repetición con mismo ID y mismo hash devuelve el resultado original sin crear otra comanda; mismo ID con otro actor o contenido se rechaza como conflicto. La unicidad se garantiza en PostgreSQL, no solo en memoria.
7. Desktop no reintenta automáticamente. Ante resultado incierto conserva el ID y body en memoria durante esa sesión, bloquea una nueva intención para ese destino y ofrece reconciliación/repetición explícita con el mismo ID. Una consulta normal no demuestra el resultado. Si la aplicación se cierra y pierde esa intención, no se promete recuperación; se requiere revisión humana. La idempotencia no sustituye autenticación, autorización, transacción ni controles de concurrencia.

### RF-03. Cocina y salón
1. Cada comanda tiene estado independiente: `Abierta` (enviada y recibida, aún no iniciada), `En Preparación` o `Lista`. Transiciones permitidas: `Abierta` → `En Preparación` → `Lista`; no se retrocede ni se reabre una comanda en 003. Solo `Abierta` y `En Preparación` aparecen en la cola activa de cocina.
2. FIFO significa ordenar por fecha de recepción del servidor UTC ascendente y por identificador ascendente en empate. Ordenar por esta fecha no implica prometer orden absoluto de commit bajo transacciones concurrentes.
3. Una transición exige versión vigente, autorización y estado anterior válido. Dos cocinas concurrentes no deben sobrescribir cambios; un conflicto actualiza la vista y exige una nueva decisión humana. Una lectura de cola no muta ni audita cada consulta.
4. Tras marcar lista, desaparece de la cola activa y sigue visible como lista en la cuenta de salón. Al agregarse una comanda nueva a una cuenta con comandas listas, solo la nueva regresa a la cola; las ya listas no se reinician.
5. Mientras el catálogo sea de demostración y caja no esté implementada, presentar claramente esa condición en el recorrido de demostración. No indicar pago ni stock validado.

### RF-04. Cuenta pendiente y compatibilidad con 001
1. El estado existente `pedido.estado` conserva el conjunto `Abierto`, `En Preparación`, `Listo`, `Pagado`, `Cancelado`; los tres primeros siguen siendo activos para unicidad y bloqueo de liberar mesa/cerrar grupo. 003 no ofrece transición a `Pagado` ni `Cancelado`.
2. Estado agregado aprobado: una cuenta con al menos una comanda `Abierta` es `Abierto`; si no hay abiertas pero alguna está `En Preparación`, es `En Preparación`; si todas sus comandas están `Lista`, es `Listo`. La primera comanda siempre nace `Abierta`; un envío nuevo devuelve la cuenta de `Listo` o `En Preparación` a `Abierto`, sin reiniciar comandas previas.
3. Un pedido activo legado sin comandas se conserva intacto y se presenta como **cuenta anterior sin detalle disponible**, en solo lectura. Su mesa o grupo queda bloqueado para nuevos envíos mientras la cuenta siga activa; no se inventan líneas ni se cambia estado, precio, responsable o fecha automáticamente. Solo una operación futura explícita y auditable podrá resolverlo. Pedidos históricos `Pagado` o `Cancelado` también se conservan sin fabricar comandas; su consulta histórica completa queda fuera del MVP. La migración no debe borrar filas ni debilitar la unicidad vigente.

### RF-05. Red y seguridad
1. Sin conexión se conserva la última información identificada como potencialmente desactualizada y se bloquean mutaciones; no se encolan ni reintentan escrituras automáticamente. Polling no constituye actividad humana ni fuerza refresh de sesión.
2. Ante timeout, red, respuesta inválida o 5xx tras una escritura, mostrar incertidumbre y bloquear acciones conflictivas; consultar datos para revisión humana **no demuestra** que la escritura terminó. El comportamiento universitario y sus límites deben ser explícitos; no se declara seguro para operación real ni recuperación tras reinicio.
3. Matriz aprobada por el usuario y validada en API en cada solicitud:

   | Rol | Consultar todas las cuentas/comandas de salón y enviar | Ver cola KDS | Cambiar estados KDS | Cambiar mesas/grupos |
   |---|---:|---:|---:|---:|
   | Jefa | Sí | Sí | Sí | Según permisos de 001 |
   | Supervisor | Sí | Sí | Sí | Según permisos de 001 |
   | Mesero | Sí, todas las mesas | No; ve estado desde cuenta | No | Según permisos de 001 |
   | Encargado de Caja | Sí, todas las mesas | No; ve estado desde cuenta | No | Según permisos de 001 |
   | Personal de Cocina | No | Sí, comandas de cocina | Sí | No |

   Cada comanda conserva al usuario que la envió. El acceso de salón no se limita al creador, para permitir continuidad entre turnos. Cocina no obtiene acceso al envío/cuentas ni a mutaciones de mesas por sus permisos KDS. Jefa/Supervisor pueden operar KDS, pero no anular líneas enviadas en 003.
4. Auditoría de creación de cuenta/envío y transición KDS dentro de la transacción correspondiente, con actor, destino/identificadores y resultado permitido; sin contraseñas, tokens ni notas libres en logs o detalles de auditoría. Fallo de auditoría revierte la mutación.

## 5. UX y accesibilidad

- Toma de comandas: categorías, productos disponibles, cantidades táctiles +/−, notas acotadas, resumen, precio y confirmación explícita. La vista distingue borrador, envío confirmado, error y resultado incierto.
- KDS: tarjetas legibles a distancia, nombre y cantidad destacados, notas visibles con texto además de color, destino y fecha; botones amplios, foco visible y comportamiento útil con teclado. No depender de parpadeo o color para comunicar información.
- Vista de cuenta: separar comandas por envío y estado; indicar lista sin suponer que fue retirada. Definir presentación y filtros para volumen alto; no eliminar historial por ocultar un ticket de la cola.
- Mantener contraste, errores y acciones visibles al tamaño mínimo, con escalado y en desconexión; validar visualmente antes de aceptar.

## 6. Criterios de aceptación del MVP

1. Una mesa libre o reservada obtiene cuenta y primera comanda atómicamente y queda ocupada; una cuenta de grupo queda asociada al grupo y no cambia sus mesas a ocupadas.
2. Envíos sucesivos o simultáneos no crean cuentas activas duplicadas ni pierden datos; cocina ve cada comanda pendiente y salón ve las listas, también con actualizaciones de otra terminal.
3. Precio y nombre históricos no cambian si cambia el catálogo; disponibilidad, cantidades, notas y desbordamientos se validan en API. Para el régimen informado, no se añade IVA: impuestos es cero y total igual a subtotal.
4. FIFO estable, transiciones/roles válidos, conflictos y comunicación de estado están cubiertos por pruebas API y Desktop; sin mutaciones para roles no autorizados.
5. Una cuenta activa bloquea liberar mesa o cerrar grupo; KDS `Lista` no significa `Pagado`. Los tests prueban carreras y preservan los contratos 001.
6. La migración preserva datos e índices existentes, rechaza estados incompatibles de forma segura y se prueba en PostgreSQL temporal. No se aplican cambios a una base persistente sin autorización y destino confirmado.
7. Se prueban red, timeout, respuestas tardías, reconciliación, sesión y accesibilidad; se documenta expresamente lo no medido o que no sea seguro para producción.
8. Repetir una intención con `envioId` y body idénticos produce exactamente una comanda y devuelve el mismo resultado; mismo ID con body distinto produce conflicto; carreras con el mismo ID no duplican. Cubrir timeout antes/después del commit y fallo de auditoría.

## 7. Condiciones no funcionales

Mantener las versiones de paquetes existentes salvo análisis de compatibilidad; validación TLS, aislamiento REST, UTC y límites de tamaño; logs sin datos sensibles. Los datos de prueba, credenciales y bases temporales se mantienen aislados. No afirmar cobertura ≥80 %, aceptación LAN física o tiempos de render sin medición específica.

## 8. Datos existentes y demostración

La tabla `pedido` puede contener registros adoptados o usados para verificar cuentas pendientes. Las migraciones nuevas no borran ni inventan su contenido. El entorno demostrativo debe crear sus propias cuentas y catálogo en una base temporal o destino explícitamente autorizado; nunca borrar o modificar bases preexistentes para facilitar la prueba.

## 9. Dependencias posteriores

Inventario/recetas, autorización administrativa y anulación enviada, caja/pago/facturación, gestión de catálogo, recuperación definitiva y despliegue real tendrán especificaciones o ampliaciones propias. Sus nombres y números todavía no están asignados; `004` y `005` mencionados en material inicial no se reservan automáticamente.

## 10. Decisiones a aprobar antes de implementar

- Matriz de roles y alcance de cuentas/comandas: **aprobada** según RF-05. Las consultas de salón abarcan todas las mesas; el estado `Lista` aparece en la cuenta para los roles de salón autorizados y no afirma retiro físico del plato.
- Semántica de estados de cuenta y comandas: **aprobada** según RF-03/RF-04; los valores y agregación son reglas acordadas para esta especificación, no cambios ya implementados en base/API.
- Visibilidad del estado listo: **aprobada** para todos los roles de salón con acceso a cuentas, no solo para quien envió la comanda. No hay todavía acción de “retirado” ni acuse de lectura.
- Política para pedidos activos heredados sin comandas: **aprobada**. Se conservan intactos, se exponen como cuenta anterior de solo lectura y bloquean nuevos envíos al destino hasta resolución explícita futura. La migración debe probarlo con PostgreSQL temporal; ante una incompatibilidad estructural que impida preservar reglas, aborta sin pérdida de datos.
- Aprobación de la línea base técnica de `plan.md`: `envioId` idempotente persistido, límites propuestos, contrato HTTP/códigos y paginación/polling. Cualquier cambio de cifras o rutas antes de implementar debe reflejarse aquí.
- Límite comercial de cantidad por línea y cantidad de productos por comanda: validar propuesta con el usuario antes de congelar; no inferir que el valor técnico propuesto representa una regla del negocio.
- Versión de catálogo/cuenta y detalle final de conflictos. Acordar el manejo de nombres/duplicados de líneas y ejemplos de máximos antes de D1/D2.
- Definir si un envío admite líneas repetidas del mismo producto cuando difieren notas; no agregarlas ni separarlas silenciosamente.
- Cálculo fiscal 003: **decidido para el régimen informado por el usuario**. No aplicar ni mostrar IVA 15 %; el total de pedido coincide con el subtotal. Si el régimen cambia, revisar la regla con el negocio/asesor competente y especificar el cambio antes de implementarlo. Los pedidos no son facturas.
- Revisión del protocolo de sondeo/frescura y paginación propuestos en `plan.md`; el aviso de lista es visible mientras la cuenta permanece abierta y no constituye entrega garantizada ni acuse.
- Riesgo universitario aceptado de escrituras inciertas, bloqueo y revisión humana, con la limitación de reinicio explícita. La clave persistida propuesta puede reproducir una respuesta si el cliente conserva la misma intención; recuperar la intención tras perder el estado local, resolverla entre terminales y operar con garantías reales requieren diseño aparte.

### Línea base técnica preparada para revisión (no implica aprobación)

`plan.md` documenta una recomendación completa para idempotencia persistida (`envioId`), contratos HTTP/ProblemDetails, límites técnicos candidatos, validación Unicode, concurrencia/locks, polling/paginación y migración no destructiva. Son propuestas técnicas que se pueden corregir en conjunto o por punto; no son requisitos comerciales confirmados ni están implementadas. Antes de D1/D2, registrar en tareas la aprobación explícita o los cambios acordados. En particular, confirmar máximos de líneas/cantidad/precio con el negocio. La política de no reintentar automáticamente y de no inferir éxito desde un GET permanece obligatoria aunque se apruebe la clave idempotente.

La aprobación de contratos no autoriza aplicar migraciones a bases persistentes ni implementar elementos fuera de alcance. La preparación documental no equivale a aceptación visual, pruebas de migración ni validación para operación real.
