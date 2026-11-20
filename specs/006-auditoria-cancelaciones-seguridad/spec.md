# Especificación propuesta 006 — Auditoría, cancelaciones y seguridad

**Estado: reanudado por autorización del usuario para el perfil universitario local.** El flujo de anulaciones está implementado; su estado efectivo, pruebas y pendientes están en `tasks.md` y `docs/validacion-006.md`. No se declara aceptación integral ni aptitud para producción. La especificación numerada 006 de reservas es independiente: `specs/006-reservaciones-mesas/`.

### Cambio aprobado: continuar la atención después de una anulación total

El usuario autorizó permitir un pedido nuevo después de anular totalmente la cuenta, tanto en mesa individual como en grupo activo, sin liberar ni desagrupar el destino. La cuenta anulada, sus movimientos y alertas permanecen históricos; el nuevo pedido tiene identidad propia y consume inventario solo al enviarse. Una anulación parcial conserva la cuenta activa y los envíos posteriores se agregan a ella. Se conservan los bloqueos de cobro pendiente, cuentas pagadas de grupo y operaciones inciertas; este cambio no autoriza reabrir pagos ni reintentar escrituras. La API valida bajo los locks de destino y cuenta, sin inferir temporalidad mediante `xmin`; usa un indicador explícito creado por la anulación total y consumido por el siguiente envío. La migración no presume que anulaciones anteriores correspondan a la ocupación actual. Consultar evidencia y límites en `tasks.md`.

## 1. Objetivo y límites

Permitir cancelar justificadamente líneas completas o comandas enviadas, con autorización de Jefa, conservar evidencia, devolver únicamente consumo demostrablemente atribuible, informar a Cocina y ofrecer consultas de auditoría autorizadas. No incluye anular cobros, reembolsos, facturación fiscal, anulación por unidades parciales, compras reales ni recuperación general de 002.

**Decisiones expresas del usuario:** solo se anula una línea completa o una comanda completa; una línea histórica sin desglose exacto de consumo se rechaza. Una comanda histórica podrá anularse íntegramente solo si hay evidencia suficiente para devolver exactamente lo no revertido. La mera existencia de movimientos agregados no demuestra que estén completos. No se inventan consumos de comandas anteriores a 005; si no se puede demostrar la devolución, rechazarla y solicitar revisión.

El usuario también eligió exigir la contraseña de Jefa aun cuando ella misma solicita la anulación; bloqueo superior separado del login, con cinco fallos por cinco minutos por par solicitante/autorizador; y cierre explícito del grupo antes de otra cuenta tras cancelación total. El diseño del bloqueo en `plan.md` no demuestra protección contra ataques distribuidos.

El usuario aprobó que un cambio no coordinado de clave HMAC para el límite superior produzca bloqueo durable de nuevas autorizaciones durante 24 horas; login permanece independiente. La instalación inicial y recuperación ante metadatos ausentes no se presuponen seguras y requieren un procedimiento explícito.

El usuario aprobó recuperar explícitamente una fila HMAC ausente con permisos administrativos, auditoría y espera mínima de 24 horas, conservando operaciones y contadores. Una fila existente no se reemplaza mediante este procedimiento; otras corrupciones siguen bloqueadas para revisión. La recuperación no autoriza ninguna operación de negocio ni modifica automáticamente una base persistente.

**Precisión conservadora aprobada:** en la primera entrega se bloquea también la anulación de comandas completas anteriores a 006 sin snapshot de consumo verificable. La existencia de movimientos agregados, sin prueba de su completitud, no autoriza una devolución automática. La posibilidad de resolverlas queda fuera de esta entrega y exige otra especificación; no se hace ajuste silencioso.

## 2. Reglas funcionales propuestas

1. Salón solicita cancelación de línea o comanda en `Abierta`, `En Preparación` o `Lista`. La API revalida identidad, permisos, pertenencia, estado, importes y elegibilidad; el cliente no es autoridad. Eliminar del borrador aún no enviado conserva reglas 003 y no constituye cancelación autorizada.
2. Requiere confirmación explícita, motivo NFC recortado de 5–250 valores escalares sin controles y contraseña de Jefa activa. Si Jefa solicita, vuelve a introducir su contraseña y queda registrada como solicitante y autorizadora, sin crear una nueva sesión.
3. Rechazar pedido `Pagado`, cobro existente, operación de cobro preparada/no terminal que pueda afectar esa cuenta, línea ya cancelada y cualquier evidencia incompleta. No modificar un cobro ni su comprobante. Serializar también contra una preparación de cobro todavía no visible.
4. Los envíos nuevos conservan evidencia inmutable de consumo por línea e ingrediente, calculada con la receta efectivamente usada al enviar. Su suma debe coincidir con el consumo agregado por comanda; sin consultar receta actual para una reversión. Repeticiones idempotentes de envío no duplican evidencia.
5. Cancelar una línea devuelve exactamente su evidencia de consumo, una sola vez. Cancelar una comanda con anulaciones previas devuelve únicamente el consumo remanente atribuible; no exceder jamás el consumo original por ingrediente. Sin evidencia inequívoca se rechaza sin efecto. Reversión positiva, stock, versión, recálculo, evidencia, alerta y auditoría de éxito comparten transacción.
6. Los importes cancelados se calculan desde cantidad y precio históricos, no desde el catálogo. El total se recalcula con líneas activas y aritmética exacta; si no queda ninguna, el pedido queda `Cancelado`. Mesa o grupo no se libera/cierra automáticamente. La transición de comanda, la elegibilidad de nuevas comandas y los índices de pedido activo deben precisarse y aprobarse antes de implementar.
7. Cada cancelación genera una alerta KDS identificable individualmente, con texto explícito «ANULADO — NO PREPARAR» y motivo; permanece pendiente hasta acuse autorizado. Varios eventos de una misma comanda no se pierden por un único acuse. Polling no garantiza entrega inmediata ni acción física de Cocina.
8. Toda lectura de auditoría está restringida a Jefa y Supervisor. La vista de anulaciones muestra destino, líneas o comanda, importe, actores, motivo, devolución y fecha; los eventos críticos muestran solo campos necesarios. No incluir secretos, tokens, contraseñas, cuerpos, notas de pedido ni referencias bancarias.
9. Ninguna escritura incierta se reintenta automáticamente; una lectura ordinaria del pedido no demuestra terminación. El protocolo debe permitir identificar y resolver la intención concreta con resultado terminal o barrera durable, sin rearmar su ejecución durante recuperación.

## 3. Roles propuestos

| Acción | Jefa | Supervisor | Mesero | Encargado de Caja | Personal de Cocina |
|---|---|---|---|---|---|
| Solicitar autorización y cancelar | Sí | Sí | Sí | Sí | No |
| Autorizar con credencial superior | Sí | No | No | No | No |
| Consultar y acusar alertas KDS | Sí | Sí | No | No | Sí |
| Consultar auditoría y anulaciones | Sí | Sí | No | No | No |

La matriz se aplica en API con sesión vigente y se revalida tras esperas; Desktop solo presenta acciones autorizadas. Revocación y sesiones concurrentes se resuelven según el protocolo de `plan.md`.

## 4. Contratos propuestos, sujetos a D0

JSON camelCase, DTOs, `ProblemDetails` con `code` y `traceId`; body máximo inicial 16 KiB, cursores opacos ligados a filtros, 25 elementos por defecto y máximo 100. 401 conserva contratos de sesión existentes; errores no enumeran usuarios ni exponen SQL o secretos.

| Ruta | Finalidad |
|---|---|
| `POST /api/seguridad/autorizar-anulacion` | Verificar credencial de Jefa y emitir autorización efímera vinculada a actor, sesión, intención y recurso. |
| `POST /api/pedidos/{idPedido}/anulaciones` | Ejecutar una intención identificada y autorizada, una sola vez. |
| `GET /api/anulaciones/operaciones/{uuid}` | Consultar resultado de la intención autorizada, no el estado genérico del pedido. |
| `POST /api/anulaciones/operaciones/{uuid}/cancelacion` | Propuesta de barrera terminal que impide una ejecución tardía, sin deshacer un resultado confirmado. |
| `POST /api/cocina/anulaciones/{idAnulacion}/acuse` | Acusar una alerta concreta; no perder otras alertas de la comanda. |
| `GET /api/cocina/alertas-cancelacion?cursor=&limite=` | Propuesta de cola independiente de alertas pendientes, incluso para comandas listas o pedidos ya cancelados. |
| `GET /api/auditoria/anulaciones` | Historial paginado por intervalo UTC. |
| `GET /api/auditoria/eventos` | Eventos autorizados, paginados por tipo y/o rango definido en D0. |

La cola KDS 003 no muestra comandas listas ni pedidos cancelados; se propone la cola separada anterior, sin prometer entrega instantánea. Códigos propuestos: 400 `VALIDATION_ERROR`; 401 códigos existentes de sesión; 403 `AUTH_PROHIBIDO` o `AUTORIZACION_SUPERIOR_INVALIDA` genérico; 404 `RECURSO_NO_ENCONTRADO` sin enumeración; 409 `CUENTA_YA_PAGADA`, `OPERACION_NO_TERMINAL`, `ITEM_YA_CANCELADO`, `EVIDENCIA_CONSUMO_INSUFICIENTE`, `OPERACION_CLAVE_EN_USO`, `TOKEN_SUPERIOR_EXPIRADO_O_USADO` y `GRUPO_CANCELADO_REQUIERE_CIERRE`; 413 `REQUEST_TOO_LARGE`; 429 `DEMASIADOS_INTENTOS_AUTORIZACION`. Un fallo de infraestructura o respuesta inválida posterior al envío no se interpreta como rechazo definitivo.

## 5. Interfaz y aceptación

Modal de autorización con resumen no editable, motivo y credencial enmascarada; advertencia clara de rechazo e incertidumbre. KDS muestra alerta con texto, contraste y acuse por evento; auditoría permite filtros y paginación. Desktop usa REST, MVVM, bindings compilados y code-behind solo de inicialización; sin referencia a API/EF/Npgsql. Limpiar referencias y campos sensibles inmediatamente cuando sea posible: las cadenas administradas y copias de transporte no garantizan borrado físico de memoria.

C4 exige regresión esencial, pruebas con PostgreSQL temporal, recorrido ficticio, revisión local del usuario y registro de evidencia en `docs/validacion-006.md`. No se afirma LAN física, ausencia total de deadlocks, recuperación exhaustiva, accesibilidad integral ni producción sin pruebas específicas.

## 6. Compatibilidad y seguridad

003/004/005 se modifican solo mediante adendas aprobadas: conservar históricos, idempotencia de envío, protección de Caja, kardex append-only y restricciones de incertidumbre. La autorización propuesta no reemplaza sesión ni TLS. BCrypt verifica el hash existente; no prometer tiempo constante de todo el flujo HTTP. No guardar credenciales, tokens ni payload sensible en logs, auditoría o diarios; normalizar y limitar el motivo y restringir su lectura.

## 7. Línea base y aprobación D0

El usuario aprobó conjuntamente `spec.md`, `plan.md` y las adendas siguientes para comenzar C1, incluida la restricción conservadora sobre históricos. La aprobación no equivale a comprobar las carreras, aplicar migraciones persistentes ni autorizar dinero real. Si la implementación evidencia incompatibilidades, detener y revisar la línea base antes de cambiar reglas.

### Adendas propuestas a módulos anteriores

- **003 Pedidos/KDS:** conservar detalles históricos, añadir cancelación lógica, recalcular por líneas activas, invalidar la versión de la comanda al cancelar una línea, mantener alertas separadas hasta acuse y exigir cierre explícito de grupo con cuenta cancelada antes de otra cuenta. El replay de envío es un recibo histórico, no la cuenta actual.
- **004 Caja:** no anular ni modificar cobros. La elegibilidad, huella, detalle y comprobante de nuevas operaciones consideran líneas activas; se conserva la interpretación v1 de las operaciones ya preparadas. Una intención no terminal bloquea la anulación y se serializa frente a ella.
- **005 Inventario:** desde 006 se guardan snapshots por línea e ingrediente junto al consumo agregado; las anulaciones elegibles agregan movimientos positivos append-only sin exceder el consumo original. No se recrean snapshots para comandas históricas ni se reconstruye consumo usando recetas actuales.

Las adendas solo entrarán en vigor al implementar y validar 006; no afirman que las capacidades ya existan.
