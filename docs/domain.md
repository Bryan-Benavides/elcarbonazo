# Diccionario de Dominio y Datos (Definitivo) — El Carbonazo

## Contexto Operativo y Fiscal
- **Negocio:** Bar Restaurante "El Carbonazo" (Managua, Nicaragua).
- **Infraestructura:** 15 mesas físicas, 8 colaboradores, ~90 productos en menú.
- **Reglas Fiscales:** IVA 15% (RD-01), Propina voluntaria 10%, Sistema Bimonetario (NIO / USD) (RD-02).

## Esquema Relacional PostgreSQL (3FN)
1. `usuario` (id_usuario [PK, SERIAL], nombre, contrasena_hash, rol, email, activo)
2. `mesa` (id_mesa [PK, SERIAL], id_grupo [FK, NULL], numero, capacidad, estado) -> Estados: 'Libre', 'Ocupada', 'Reservada'
3. `producto` (id_producto [PK, SERIAL], nombre, precio, categoria, disponible)
4. `ingrediente` (id_ingrediente [PK, SERIAL], nombre, stock_actual, stock_minimo, unidad_medida, alerta_stock_bajo)
5. `receta` (id_receta [PK, SERIAL], id_producto [FK, UNIQUE], nombre, instrucciones)
6. `detalle_receta` (id_detalle [PK, SERIAL], id_receta [FK], id_ingrediente [FK], precio_unitario, cantidad_ingrediente)
7. `pedido` (id_pedido [PK, SERIAL], id_mesa [FK], id_usuario [FK], fecha_hora, subtotal, impuestos, total, estado) -> Estados: 'Abierto', 'En Preparación', 'Listo', 'Pagado', 'Cancelado'
8. `detalle_pedido` (id_detalle [PK, SERIAL], id_pedido [FK], id_producto [FK], cantidad, precio_unitario, subtotal, notas)
9. `factura` (id_factura [PK, SERIAL], id_pedido [FK, UNIQUE], id_usuario [FK], fecha, subtotal, impuestos, total, metodo_pago, referencia, tasa_cambio)