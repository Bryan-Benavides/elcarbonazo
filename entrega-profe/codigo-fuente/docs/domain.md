# Diccionario de Dominio y Datos (Definitivo) — El Carbonazo

## Contexto Operativo y Fiscal
- **Negocio:** Bar Restaurante "El Carbonazo" (Managua, Nicaragua). El catálogo de 90 productos y los precios preparados para la defensa son datos suministrados por el usuario y no una verificación independiente de la oferta o precios vigentes del restaurante.
- **Infraestructura:** 15 mesas físicas, 8 colaboradores, ~90 productos en menú.
- **Referencias generales del modelo:** IVA general 15% (RD-01), propina voluntaria 10%, sistema bimonetario NIO/USD (RD-02). **Aplicación al negocio según información del usuario:** opera bajo régimen de cuota fija y no añadirá IVA 15 % en el alcance 003. Los pedidos de 003 usarán `impuestos = 0` y `total = subtotal`; no son facturas. Esta información comercial no ha sido verificada legalmente. Si cambia el régimen, confirmar con responsable/asesor competente y revisar la especificación antes de cambiar el sistema.
- **Red local:** La arquitectura contempla API mediante HTTPS en hostname LAN fijo (referencia: `elcarbonazo-api.local`); la entrega para defensa local no acredita instalación ni validación física de LAN.
- **Configuración:** Direcciones, certificados, contraseñas y claves no se compilan ni se guardan como secretos en archivos versionados.
- **Desktop Windows:** `ApiBaseUrl` e `idInstalacion` residen en `%ProgramData%\ElCarbonazo\desktop.json`; el instalador los crea y usuarios normales solo tienen lectura.
- **Fallo de configuración:** Si ese archivo falta o es inválido en producción, Desktop bloquea el login y muestra un error no editable; no usa localhost ni permite introducir otra URL.
- **Fallo TLS:** Un certificado inválido, no confiable, vencido o con hostname incorrecto bloquea la aplicación como error de seguridad, detiene tareas y limpia la sesión local; nunca ofrece bypass ni lo trata como desconexión recuperable.
- **Migraciones:** En producción se aplican explícitamente durante instalación con un rol administrativo separado; la API runtime no modifica el esquema.
- **Compatibilidad:** Si existe una tabla `pedido` no administrada por EF, la migración la adopta únicamente tras validar esquema y datos dentro de la transacción; una incompatibilidad aborta sin modificar ni eliminar información.
- **Respaldos:** Producción genera un respaldo PostgreSQL diario, cifrado y almacenado en otro disco o NAS local; conserva 30 días y verifica una restauración aislada cada mes.
- **Tiempo:** Backend y PostgreSQL persisten, comparan y emiten instantes en UTC; Desktop convierte únicamente para mostrar mediante la zona `America/Managua`.

## Modelo relacional de dominio (referencia; no equivale al esquema desplegado)
> Esta lista es conceptual, no describe necesariamente el esquema desplegado. Las migraciones 003 y 004 incorporan productos, comandas, detalles y caja; 005 añade ingredientes, recetas y kardex, con aceptación para el perfil universitario local y límites documentados. Factura fiscal no está implementada. Consultar entidades y migraciones antes de asumir que una tabla existe en una base concreta.
> `specs/005-inventario-escandallo/` define el modelo vigente de ingredientes, receta y kardex; ante diferencias prevalecen esa especificación y el esquema efectivo, no esta lista histórica conceptual.

1. `usuario` (id_usuario [PK, SERIAL], nombre [UNIQUE], contrasena_hash, rol, email [UNIQUE], activo, intentos_fallidos, bloqueo_hasta [NULL])
2. `sesion_usuario` (id_sesion [PK, UUID], id_usuario [FK], fecha_creacion, ultima_actividad, expiracion_absoluta, fecha_revocacion [NULL])
3. `token_refresh_sesion` (id_token [PK, BIGSERIAL], id_sesion [FK], token_hash [UNIQUE], fecha_emision, fecha_consumo [NULL])
4. `grupo_mesa` (id_grupo [PK, SERIAL], id_usuario_creacion [FK], id_usuario_cierre [FK, NULL], fecha_creacion, fecha_cierre [NULL], activo)
5. `grupo_mesa_miembro` (id_grupo [PK, FK], id_mesa [PK, FK], fecha_asignacion)
6. `mesa` (id_mesa [PK, SERIAL], id_grupo [FK -> grupo_mesa.id_grupo, NULL], numero, capacidad, estado) -> Estados: 'Libre', 'Ocupada', 'Reservada', 'Agrupada'
7. `producto` (id_producto [PK, SERIAL], nombre, precio, categoria, disponible)
8. `ingrediente` (id_ingrediente [PK, SERIAL], nombre, stock_actual, stock_minimo, unidad_medida, alerta_stock_bajo)
9. `receta` (id_receta [PK, SERIAL], id_producto [FK, UNIQUE], nombre, instrucciones)
10. `detalle_receta` (id_detalle [PK, SERIAL], id_receta [FK], id_ingrediente [FK], precio_unitario, cantidad_ingrediente)
11. `pedido` (id_pedido [PK, SERIAL], id_mesa [FK, NULL], id_grupo [FK, NULL], id_usuario [FK], fecha_hora, subtotal [NIO], impuestos [NIO], total [NIO], estado) -> Estados: 'Abierto', 'En Preparación', 'Listo', 'Pagado', 'Cancelado'
12. `detalle_pedido` (id_detalle [PK, SERIAL], id_pedido [FK], id_producto [FK], cantidad, precio_unitario, subtotal, notas)
13. `factura` (id_factura [PK, SERIAL], id_pedido [FK, UNIQUE], id_usuario [FK], fecha, subtotal, impuestos, total, metodo_pago, referencia, tasa_cambio)
14. `evento_auditoria` (id_evento [PK, BIGSERIAL], fecha_hora, tipo_evento, resultado, id_usuario [FK, NULL], id_sesion [FK, NULL], direccion_ip [NULL], id_instalacion [UUID, NULL], tipo_entidad [NULL], id_entidad [NULL], trace_id, detalles [JSONB])

## Reglas de Auditoría y Logs

> **006 aprobado para implementación, aún no implementado:** cancelaciones autorizadas de líneas completas o comandas y devolución de consumo solo con evidencia histórica suficiente están especificadas en `specs/006-auditoria-cancelaciones-seguridad/`. No sustituye las reglas vigentes de 003–005 hasta su implementación ni autoriza migraciones persistentes.
- Los eventos funcionales y de seguridad críticos se conservan en una tabla de auditoría PostgreSQL dedicada.
- Se auditan resultados de login, bloqueo de cuenta, refresh, logout, revocación por nuevo login, bootstrap, cambios de estado de mesa y creación/cierre de grupos.
- Del rate limit de login se conserva únicamente el primer rechazo por IP y ventana de un minuto como `LoginRateLimitActivado`; los demás no crean filas adicionales.
- Las consultas GET, el polling, los reportes periódicos de actividad y otras lecturas no generan registros de auditoría.
- Cada mutación auditable y su evento se guardan en la misma transacción; si el evento no puede persistirse, la mutación completa se revierte.
- La auditoría es append-only: la aplicación puede insertar y consultar, pero no actualizar ni eliminar registros; una corrección genera un evento adicional.
- Los eventos permanecen al menos 5 años en la base operativa; cualquier archivo y purga posterior se realiza fuera de la aplicación con credenciales administrativas separadas.
- Los logs técnicos son estructurados y sirven para diagnóstico, rendimiento y fallos; no sustituyen ni duplican como fuente de verdad el historial de auditoría.
- En producción se escriben como JSON rotativo en disco y también en consola, sin depender de servicios externos o Internet.
- Los archivos rotan diariamente, cada archivo diario se limita a 100 MB y se conservan 30 días; la consola continúa disponible si el archivo alcanza su límite.
- En producción la aplicación usa nivel `Information` y los componentes Microsoft/EF/Npgsql `Warning`; las lecturas exitosas y el polling no se registran individualmente.
- Desktop conserva logs técnicos JSON en `%ProgramData%\ElCarbonazo\logs`, con máximo 20 MB diarios y retención de 7 días; registra cambios de conectividad/fallos, no cada polling exitoso.
- Ningún mecanismo registra contraseñas, refresh tokens, JWT completos, claves, cadenas de conexión ni secretos de certificados.
- `detalles` contiene únicamente campos permitidos por tipo de evento, por ejemplo estado anterior/nuevo o IDs de mesas; nunca cuerpos HTTP completos ni serialización libre de entidades.
- Un login fallido sin `id_usuario` registra únicamente el `nombre` intentado después de NFC → Trim → minúsculas dentro de `detalles`; no conserva el valor original ni ningún otro campo del request.
- Para eventos HTTP se registra la dirección IP observada por la API y, cuando Desktop lo envía, un UUID estable de instalación; este UUID sirve solo para trazabilidad y nunca para autenticar o autorizar.

## Reglas de Agrupación de Mesas
- Un grupo activo contiene entre 2 y 15 mesas; una mesa solo puede pertenecer a un grupo activo.
- Solo mesas en estado `Libre`, sin grupo y sin pedidos pendientes pueden agruparse.
- Crear el grupo asigna el mismo `id_grupo` y estado `Agrupada` a todas sus mesas en una sola transacción.
- La misma transacción inserta un registro inmutable en `grupo_mesa_miembro` por cada mesa; cerrar el grupo no elimina esas relaciones históricas.
- El grupo conserva el usuario creador y, al cerrarse, el usuario de cierre; ambos usan FK restrictiva y no dependen de la retención de auditoría.
- La creación devuelve el grupo y las mesas completas con sus versiones nuevas para que Desktop actualice inmediatamente.
- `Agrupada` exige `id_grupo` no nulo; cualquier otro estado exige `id_grupo` nulo.
- No se puede cerrar un grupo si el grupo tiene un pedido en estado `Abierto`, `En Preparación` o `Listo`; por el destino XOR, la cuenta compartida referencia `id_grupo`, no las mesas integrantes.
- Cerrar el grupo deja sus mesas en `Libre`, limpia `id_grupo` y registra `activo = false` y `fecha_cierre`; el grupo no se elimina.
- Repetir el cierre de un grupo existente ya inactivo responde sin error y no genera otra escritura ni auditoría; un ID inexistente sí se considera no encontrado.
- Después de cualquier 204 de cierre, Desktop consulta mesas inmediatamente y no reactiva acciones hasta obtener versiones actuales.
- Desktop permite a Jefa, Encargado de Caja, Mesero y Supervisor seleccionar mesas elegibles, crear el grupo y cerrarlo; Personal de Cocina únicamente visualiza el estado agrupado.
- La selección inicia con el botón explícito `Agrupar mesas`, admite solo mesas `Libre`, muestra el total seleccionado y exige Confirmar o Cancelar.
- Pulsar una mesa `Agrupada` muestra el ID y todas las mesas del grupo; los roles autorizados pueden elegir `Cerrar grupo` y deben confirmar antes de enviarlo.
- El polling continúa durante la selección: Desktop conserva IDs, usa las versiones más recientes visibles y retira con aviso cualquier mesa que deje de estar `Libre` o sin grupo.

## Reglas de Estado de Mesa
- `numero` está entre 1 y 15 y es único; `capacidad` está entre 1 y 20.
- `Jefa`, `Encargado de Caja`, `Mesero` y `Supervisor` pueden cambiar el estado de una mesa; `Personal de Cocina` tiene acceso de solo lectura.
- Fuera del modo de agrupación, pulsar una mesa individual abre su detalle y muestra solo Reservar, Ocupar o Liberar cuando la transición y el rol lo permitan; Liberar exige confirmación.
- Transiciones permitidas: `Libre` → `Reservada`/`Ocupada`; `Reservada` → `Libre`/`Ocupada`; `Ocupada` → `Libre` solo sin pedidos pendientes.
- Solicitar el mismo estado con versión vigente es idempotente: devuelve la mesa actual sin escritura, nueva versión ni auditoría. Una versión obsoleta se rechaza antes de evaluar el no-op.
- `Agrupada` solo se asigna al crear un grupo y solo vuelve a `Libre` al cerrar ese grupo.
- Abrir el primer pedido individual cambia `Libre` o `Reservada` a `Ocupada` en la misma transacción; abrir un pedido de grupo conserva `Agrupada`.
- Pagar o cancelar el último pedido activo habilita la liberación, pero nunca libera automáticamente la mesa ni cierra el grupo.
- Cada mesa usa el token interno PostgreSQL `xmin` como versión de concurrencia; una actualización basada en una versión obsoleta se rechaza sin sobrescribir cambios.
- Ante un conflicto de versión, Desktop recarga el estado y exige una nueva confirmación humana; nunca reaplica automáticamente la intención obsoleta.
- La creación de un grupo valida las versiones de todas las mesas y es atómica: cualquier conflicto cancela la operación completa.
- El cierre bloquea primero el grupo y luego sus mesas ordenadas por `id_mesa`, revalida pedidos pendientes y confirma todo en una transacción; la apertura futura de pedidos respeta el mismo orden de locks.
- Desktop nunca solapa consultas de polling; usa 2 segundos en operación normal y backoff 2→5→10→30 segundos ante fallos, mostrando estado sin conexión y recuperándose automáticamente.
- Mientras esté sin conexión, Desktop conserva la última información solo para lectura, deshabilita cambios y no encola operaciones para ejecutarlas después.
- Desktop limita lecturas a 5 segundos y login/mutaciones a 10 segundos; una escritura con timeout nunca se reintenta automáticamente. En el prototipo se avisa del resultado incierto y se solicita revisión humana tras consultar mesas; una lectura no demuestra que terminó la escritura. El protocolo recuperable 002 se pospone para una fase futura.

## Reglas de Pedido
- `specs/003-pedidos-kds/` separa cuenta (`pedido`) y envíos a cocina (`comanda`). Existen catálogo, consulta, envío, cola FIFO y transiciones KDS, con vistas Desktop de salón y cocina. El estado de aceptación y las tareas pendientes se mantienen en `specs/003-pedidos-kds/tasks.md`; no asumir que una migración se aplicó a una base persistente.
- Estados acordados para la propuesta 003: cada comanda pasa `Abierta` → `En Preparación` → `Lista`. La cuenta activa está `Abierto` si alguna comanda sigue Abierta; si no, `En Preparación` si alguna está en ese estado; y `Listo` solo cuando todas están Listas. Un nuevo envío añade otra comanda Abierta y devuelve la cuenta a Abierto, sin reiniciar las anteriores. `Listo` sigue siendo pedido activo: no equivale a pago, retiro ni autorización para liberar mesa/grupo.
- Matriz acordada para 003 e implementada en API: Jefa, Supervisor, Mesero y Encargado de Caja pueden consultar cuentas/comandas y enviar pedidos. Jefa, Supervisor y Personal de Cocina pueden ver la cola KDS y cambiar estado de comanda. Mesero y Encargado de Caja no operan la cola; Cocina no envía pedidos ni modifica mesas/grupos. La API autoriza cada solicitud.
- Cuenta activa heredada sin comandas: la API la conserva intacta, la muestra como cuenta anterior de solo lectura y bloquea nuevos envíos al destino hasta resolución futura explícita y auditable. No se inventan productos, detalle ni pago; la presentación Desktop queda pendiente.
- En 003, cada comanda nueva representa un envío separado de una cuenta activa; no modifica silenciosamente comandas enviadas. El catálogo de demostración no representa inventario, la anulación de artículos enviados queda pospuesta y no se simula pago ni liberación automática. El modelo EF y la migración aditiva relacionan `detalle_pedido` con comanda sin recrear `pedido` ni inventar datos históricos.
- Cada pedido pertenece exactamente a una mesa individual o a un grupo: uno y solo uno de `id_mesa` e `id_grupo` debe tener valor.
- Una mesa o grupo puede tener como máximo un pedido activo (`Abierto`, `En Preparación` o `Listo`).
- Los pedidos de un grupo representan una única cuenta compartida por todas sus mesas.
- `subtotal`, `impuestos` y `total` se almacenan en córdobas (NIO) con precisión `NUMERIC(12,2)`; 003/004 no incluyen conversión a USD.
- Las FK desde `pedido` hacia `mesa`, `grupo_mesa` y `usuario` usan borrado restringido para conservar el historial.
- Un pedido `Pagado` o `Cancelado` no se considera pendiente; los demás estados bloquean la liberación de la mesa o el cierre del grupo.
- Los importes son no negativos y `total = subtotal + impuestos`; el alcance 003/004 no calcula propina ni factura fiscal.

## Caja y Cobros — Módulo 004

- **Estado:** existe implementación de caja, cobros, comprobantes y migración 004; el avance y las verificaciones pendientes están en `specs/004-caja-cobros/tasks.md` y `docs/validacion-004.md`. La demostración no autoriza dinero real ni constituye validación fiscal.
- Se propone una caja compartida con un solo turno abierto. Jefa y Encargado de Caja pueden abrir, registrar cobros y cerrar; abridor, cobrador y cerrador se registran separadamente. Logout no cierra turno; uno cerrado no se reabre ni modifica. Fondo inicial admite C$0.00 con confirmación.
- Solo se cobra una cuenta completa con importe positivo y detalle verificable, cuando todas sus comandas están `Lista`. Las cuentas no elegibles, heredadas sin comandas, anómalas o de total cero se muestran bloqueadas con motivo. API revalida total/estado y usa historial de 003, no el catálogo actual.
- Alcance demo: NIO, `impuestos = 0`, sin propina, FX ni egresos. Efectivo esperado al cierre = fondo inicial + efectivo neto recibido (recibido menos vuelto) de cobros confirmados. Tarjeta/transferencia requieren verificación humana externa y referencia; no hay integración bancaria ni datos completos de tarjeta.
- Al cerrar se muestra la cantidad de cuentas pendientes y se advierte que no quedan pagadas; para revisar estados y detalles se reutiliza el listado paginado de cuentas. El conteo de un turno cerrado es histórico y el listado es una consulta independiente que también incluye cuentas anteriores.
- El cobro no libera mesa ni cierra grupo. Mesa pagada se libera explícitamente. El grupo pagado se cierra explícitamente antes de crear una cuenta nueva: **esto cambia la regla de 003 que permite envío posterior al mismo grupo activo** y requiere cambio contractual y pruebas aprobadas antes de implementar; no altera cuentas históricas.
- Cobro, `pedido=Pagado`, snapshot interno, auditoría y resultado durable deberán ser atómicos. Comprobante es demostrativo/no fiscal y no pide datos del cliente. No hay anulación, edición, reembolso ni corrección automática; no hay impresión/PDF.
- Recuperación durable de apertura/cobro/cierre es requisito de diseño, no prueba de preparación para producción. No reintentar automáticamente escrituras inciertas ni interpretar GET como confirmación. Operaciones no terminales bloquean acciones conflictivas y cierre.
- Antes de operación real se deben validar requisitos fiscales/documentales con el negocio y asesor competente, procedimientos de error/anulación, adquirentes/conciliación, seguridad, privacidad, backup/restauración, LAN física y aceptación del restaurante. Ver §7 de la especificación 004.

## Reglas de Sesión
- El access token JWT dura 5 minutos e incluye `sub`, `rol` y `sid` (identificador de sesión).
- El JWT se firma con HS256 y una clave aleatoria externa de al menos 32 bytes; la API no arranca con una clave ausente, corta o inválida.
- La validación JWT permite como máximo 30 segundos de tolerancia de reloj; no usa el valor predeterminado de cinco minutos.
- La sesión vence tras 15 minutos sin interacción humana y tiene una duración máxima absoluta de 8 horas.
- Desktop informa interacción humana mediante un endpoint de actividad autenticado, como máximo una vez por minuto; el polling y otras tareas automáticas no actualizan `ultima_actividad`.
- Si recibe reportes adicionales dentro del mismo minuto, la API responde normalmente con las expiraciones vigentes, pero no vuelve a escribir `ultima_actividad`.
- Si el access token vence mientras solo existe polling, Desktop no usa el refresh automáticamente: pausa consultas, conserva solo lectura y espera interacción humana para renovar.
- Ante interacción humana, Desktop renueva antes de continuar si el access token está vencido o le quedan 60 segundos o menos; renovaciones concurrentes comparten una única operación.
- Si una acción humana recibe exactamente `AUTH_ACCESS_TOKEN_EXPIRADO`, Desktop hace refresh y reintenta esa acción una sola vez; no reintenta otros errores ni timeouts.
- Login, refresh y reporte de actividad exponen por separado `inactivityExpiresAt` y `absoluteSessionExpiresAt`, siempre como instantes UTC.
- Esas respuestas incluyen `serverTime` UTC para que Desktop estime el desfase de su reloj y programe avisos con tiempo del servidor.
- Un minuto antes de `inactivityExpiresAt`, Desktop muestra cuenta regresiva con Continuar/Cerrar sesión; solo Continuar registra interacción y, sin respuesta, la sesión local termina al vencer.
- Cinco minutos antes de `absoluteSessionExpiresAt`, Desktop muestra un aviso persistente no extensible; al vencer limpia la sesión y exige login.
- Cada usuario puede tener una sola sesión activa; un nuevo login revoca inmediatamente la anterior.
- El refresh token es aleatorio, se almacena únicamente como hash SHA-256 y rota en cada renovación.
- Cada refresh token contiene 32 bytes generados criptográficamente y se transporta como Base64URL sin padding.
- Los hashes de refresh emitidos y consumidos se conservan en `token_refresh_sesion` para reconocer reusos sin guardar secretos en claro.
- El historial de hashes se elimina 30 días después de que la sesión quede revocada o venza efectivamente; la auditoría relacionada conserva su retención de 5 años.
- Reutilizar un refresh token ya consumido se considera posible robo y revoca inmediatamente la sesión completa.
- Cada petición autenticada valida que `sid` siga activo para que logout y nuevo login tengan revocación inmediata.
- Cerrar sesión informa `fecha_revocacion`; las sesiones se conservan como historial y la FK a usuario usa borrado restringido.
- Al cerrar Desktop normalmente se intenta logout con un tiempo acotado y siempre se eliminan los tokens en memoria; una caída o falta de red deja que el servidor venza la sesión por inactividad.
- MesasView muestra usuario/rol y un botón `Cerrar sesión`; tras confirmar, detiene tareas, intenta logout, limpia tokens y vuelve a LoginView aunque la API no responda.
- Una credencial ausente/inválida o una sesión vencida responde 401; un usuario autenticado cuyo rol no permite la operación responde 403. Desktop nunca intenta renovar por un 403.

## Reglas de Identidad de Usuario
- `nombre` y `email` son obligatorios y únicos.
- Ambos se almacenan normalizados en orden Unicode NFC → espacios exteriores eliminados → minúsculas; `email` admite como máximo 254 caracteres y debe tener sintaxis válida comprobada localmente, sin DNS.
- `nombre` contiene de 3 a 50 caracteres, empieza y termina con letra o número Unicode y solo admite como separadores internos simples espacio, punto, guion o guion bajo.
- Login y validación de email no distinguen mayúsculas: `Admin`, `ADMIN` y `admin` representan la misma identidad.
- PostgreSQL aplica índices únicos y CHECK constraints para impedir valores no normalizados aunque se escriba fuera de la API.
- Producción no contiene credenciales predeterminadas; la primera usuaria con rol `Jefa` se crea mediante bootstrap únicamente cuando `usuario` está vacío o contiene solo la fila heredada neutralizada, que se reutiliza conservando su ID.
- La migración posterior a `InitialCreate` detecta solo la fila que aún coincide exactamente con el seed histórico conocido, la desactiva y neutraliza su hash sin eliminarla ni alterar usuarios modificados.
- `admin/admin123` se permite únicamente como dato local cuando el entorno es `Development`.
- Toda contraseña nueva o restablecida en un entorno distinto de `Development` debe contener al menos 10 caracteres y como máximo 64 bytes en UTF-8; el bootstrap aplica la misma regla.
- No se exigen categorías de mayúsculas, minúsculas, números o símbolos; se permiten frases de contraseña y la validación se basa en longitud.
- Las contraseñas se comparan literalmente y nunca se normalizan, recortan ni convierten entre mayúsculas y minúsculas.
- Las contraseñas se almacenan con BCrypt y sal única; el factor de trabajo predeterminado es 12 y puede configurarse dentro del rango permitido 10–15.
- Después de un login válido, si el hash usa un costo inferior al configurado, se recalcula con el costo vigente antes de completar la autenticación.
- Las contraseñas nuevas y de bootstrap se rechazan si coinciden con una lista local mínima de valores comunes u obvios; esta validación funciona sin Internet.
- Cinco intentos fallidos consecutivos bloquean nuevos logins de la cuenta durante 5 minutos; el estado se persiste y un login válido reinicia el contador.
- Al vencer los 5 minutos, el contador se reinicia antes de evaluar el siguiente intento; un nuevo fallo cuenta como 1 de 5.
- Una cuenta bloqueada y unas credenciales inválidas producen el mismo error externo para no revelar nombres de usuario existentes.
- Las rutas de usuario inexistente, inactivo o bloqueado verifican contra un hash señuelo BCrypt con el costo configurado para reducir diferencias temporales observables.
- Login admite como máximo 10 intentos por minuto y dirección IP, sin cola; los excedentes se rechazan antes de BCrypt.
- Altas de empleados, cambios de rol, desactivación y restablecimiento de contraseña pertenecen a un módulo posterior; el módulo 001 solo crea la primera Jefa mediante bootstrap.
