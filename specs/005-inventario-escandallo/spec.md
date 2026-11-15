# Especificación 005 — Inventario, escandallo y descuento automático

**Estado: D0 aprobado como línea base para el perfil universitario local aislado.** El usuario autorizó las decisiones recomendadas y las precisiones técnicas están documentadas en `plan.md`. Los cambios funcionales requieren nueva aprobación. No autoriza migraciones ni cambios en datos persistentes. Offline-first, sin dinero real ni afirmación de aptitud operativa.

## 1. Objetivo y límites

Administrar ingredientes, recetas y movimientos, y descontar inventario al enviar una comanda de 003. La API es autoridad de stock y permisos; Desktop utiliza REST. Caja 004 conserva sus reglas y nunca vuelve a descontar al cobrar. El catálogo `disponible` no garantiza stock. No se incluyen compras a proveedores, costos, conversiones automáticas de unidades, reservas, anulaciones de comandas, devolución automática de consumo, integración bancaria, operación real ni recuperación durable general de 002.

Las comandas anteriores a 005 no generan consumos retroactivos ni alteraciones históricas. La migración no inventa recetas ni stock. Preparar datos ficticios requiere un destino aislado expresamente autorizado.

## 2. Decisiones D0 aprobadas

1. **Receta obligatoria:** todo producto enviado requiere receta activa con al menos una línea; incluso una bebida embotellada se representa mediante un ingrediente. Sin receta completa se rechaza el envío, sin efectos. Esto cambia el comportamiento vigente de 003 una vez implementado 005; no se habilita retroactivamente por esta documentación.
2. **Unidad estable:** cada ingrediente tiene una unidad canónica, sin conversiones. Puede cambiarse solo mientras no exista ningún movimiento ni referencia en recetas; para otra unidad se crea un ingrediente nuevo. La inactivación no borra historia y se rechaza si una receta activa lo utiliza; una receta inactiva o sin líneas no habilita envíos.
3. **Existencia inicial:** se crea ingrediente con stock cero; se carga mediante `EntradaCompra` ficticia explícita y auditada. Para ajustes se exige motivo; para entrada, motivo opcional. Nunca se edita stock directamente.
4. **Lectura por roles:** Jefa administra y consulta todo; Supervisor y Personal de Cocina consultan ingredientes, alertas y kardex, sin mutar ni consultar/editar recetas; Mesero y Encargado de Caja no acceden a inventario ni recetas. En un rechazo de envío los roles autorizados de salón reciben únicamente nombres, unidades y cantidades faltantes necesarios para corregirlo.
5. **Resolución de incertidumbre:** para las nuevas escrituras administrativas, sin reintento automático ni garantía de recuperación tras reinicio; conservar bloqueo local de acciones conflictivas, avisar y exigir revisión humana. Una lectura no demuestra la terminación de una escritura. No reutilizar ni modificar el diario de Caja.

## 3. Reglas funcionales

- `ingrediente`: nombre NFC y trim, único sin distinción de mayúsculas según una normalización definida y probada en API y base; unidad de `lb`, `kg`, `oz`, `g`, `unidad`, `ml`, `lt`; stock y mínimo exactos de 0 a 99.999.999,9999, hasta cuatro decimales; estado activo.
- `Agotado` si stock es cero; `StockBajo` si es positivo y no supera mínimo; `Normal` en otro caso. Alertas y conteos consideran ingredientes activos; filtros y alcance se explicitan.
- Una receta por producto, activa o inactiva, con notas opcionales hasta 500 caracteres y líneas únicas por ingrediente; cantidades positivas de hasta cuatro decimales, en la unidad del ingrediente. Guardado completo atómico, con versión opaca para evitar sobrescrituras; selección solo de ingredientes activos. Rechazar cantidades no representables, duplicados, recetas activas vacías y desbordamientos.
- Al enviar, tras resolver `envioId` conforme a 003, agregar por ingrediente todas las líneas, incluyendo productos repetidos: cantidad entera pedida × cantidad de receta. Sin redondeos; comprobar límites antes de persistir. Revalidar receta, ingredientes activos y stock bajo locks. Si falta receta o stock, rechazar todo, sin cuenta nueva, comanda, movimiento, auditoría de éxito ni ocupación parcial.
- Por ingrediente consumido se inserta un movimiento negativo por comanda, con saldo anterior/posterior, pedido, comanda y actor. La misma transacción confirma stock, comanda, detalle, resultado idempotente y auditoría. Repetición de envío confirmado con mismo actor/hash reproduce el resultado **sin nuevo consumo**, aunque cambie receta o stock después. Un rechazo sin commit no crea resultado confirmado; tras corregir el borrador, crear intención nueva.
- `EntradaCompra` y `AjustePositivo` son positivos; `AjusteNegativo` y `ConsumoPedido` negativos. Ajustes requieren motivo no vacío hasta 250 caracteres; movimiento manual no puede dejar saldo negativo. Kardex es append-only y conserva origen, actor, instante y saldos; correcciones son movimientos nuevos.
- Registrar en la misma transacción `IngredienteCreado`, `IngredienteActualizado`, `RecetaGuardada`, `MovimientoInventarioRegistrado` y `ConsumoInventarioPorPedido`, con detalles permitidos mínimos y sin payloads ni secretos. Si falla auditoría, rollback completo. Las lecturas no generan auditoría.

## 4. Contratos HTTP propuestos

JSON camelCase, autenticación y autorización vigentes, DTOs explícitos, UTC, `ProblemDetails` con `code` y `traceId`; 401 conserva códigos de sesión actuales. Paginación por cursor opaco ligado a filtros, límite por defecto 25 y máximo 100; páginas no prometen snapshot. Límites propuestos: 16 KiB por ingrediente/movimiento, 32 KiB por receta, manteniendo 64 KiB en envíos 003.

| Ruta | Función | Roles |
|---|---|---|
| GET `/api/inventario/ingredientes?cursor=&limite=&estadoAlerta=&busqueda=` | Lista y estado | Jefa, Supervisor, Personal de Cocina |
| POST `/api/inventario/ingredientes` | Crear con stock cero | Jefa |
| PUT `/api/inventario/ingredientes/{id}` | Editar metadatos con versión | Jefa |
| POST `/api/inventario/movimientos` | Entrada o ajuste manual | Jefa |
| GET `/api/inventario/movimientos?idIngrediente=&cursor=&limite=` | Kardex | Jefa, Supervisor, Personal de Cocina |
| GET `/api/inventario/alertas` | Conteos y página de alertas activas | Jefa, Supervisor, Personal de Cocina |
| GET `/api/recetas/productos?cursor=&limite=&busqueda=` | Catálogo con indicador de receta, sin líneas | Jefa |
| GET `/api/recetas/producto/{idProducto}` | Receta, líneas y versión; ausencia explícita | Jefa |
| PUT `/api/recetas/producto/{idProducto}` | Crear/reemplazar con precondición de versión o ausencia | Jefa |

Errores: 400 `VALIDATION_ERROR`, 403 `AUTH_PROHIBIDO`, 404 `HTTP_NO_ENCONTRADO` como en 003, 409 `INGREDIENTE_DUPLICADO`, `STOCK_INSUFICIENTE`, `INGREDIENTE_INACTIVO`, `INGREDIENTE_EN_USO`, `RECETA_NO_DISPONIBLE`, `VERSION_CONFLICT`, `UNIDAD_EN_USO`, 413 `REQUEST_TOO_LARGE`. Se conservan los códigos existentes de 003/004. `STOCK_INSUFICIENTE` devuelve solo faltantes autorizados y cantidades exactas, sin revelar secretos ni información interna.

## 5. Interfaz y aceptación

E-04 permite a Jefa buscar productos, ver si tienen receta, editar líneas de ingredientes activos y cantidades de cuatro decimales, prevenir duplicados y confirmar. S-05 presenta resumen, filtros, estados con texto y color, kardex y formularios de entrada/ajuste solo para Jefa. Pedidos muestra faltantes o receta no disponible sin perder el borrador ni crear una intención automática nueva.

Desktop mantiene MVVM, bindings compilados y code-behind solo de inicialización; sin referencia a API, EF o Npgsql. Respetar sesión, lecturas obsoletas, desconexión y ausencia de reintentos automáticos. La aceptación universitaria exige pruebas esenciales, recorrido ficticio completo y revisión práctica local por el usuario; las mediciones y pruebas no realizadas se declaran expresamente.
