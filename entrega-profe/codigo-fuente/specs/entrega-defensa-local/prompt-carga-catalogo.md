# Encargo de implementación: carga del catálogo de defensa

Preparar una carga explícita y verificable del catálogo aportado por el usuario:
`catalogo-productos.csv`, `catalogo-ingredientes.csv` y `recetas.txt`.
El destino autorizado es PostgreSQL local, base `elcarbonazo`; el usuario confirmó
haber creado un respaldo. No mostrar ni registrar credenciales.

## Requisitos

1. Validar la integridad completa de las fuentes antes de escribir: 90 productos,
   116 ingredientes, 90 recetas, cantidades decimales exactas, unidades canónicas,
   referencias existentes y ausencia de duplicados. La mantequilla está en gramos.
2. Validar explícitamente servidor local, nombre efectivo de base y autorización.
   Bloquear las tablas correspondientes y abortar si ya existe cualquier contenido
   incompatible en productos, ingredientes, recetas o movimientos. Conservar
   usuarios, mesas, pedidos, grupos, caja y auditoría existentes.
3. Cargar en una sola transacción. Crear ingredientes con stock cero y registrar
   las existencias mediante `EntradaCompra` y auditoría, no editar stock sin
   movimiento. Crear recetas activas con sus líneas y registrar auditoría.
   Determinar explícitamente un usuario Jefa activo para las operaciones auditadas.
4. No repetir automáticamente ninguna escritura incierta. Una segunda ejecución
   solo puede ser un no-op si el conjunto entero coincide exactamente; en caso
   contrario, abortar y conservar todos los datos.
5. Probar primero contra PostgreSQL temporal creado por las pruebas: éxito,
   repetición, datos inválidos, tablas parcialmente pobladas, rollback y
   preservación de pedidos preexistentes. Probar envío → Cocina → Caja y comprobar
   un solo descuento, sin cobros reales. No presentar anulaciones como terminadas.
6. Solo después de pasar las pruebas, aplicar a `elcarbonazo`, informar los conteos
   y comprobar lecturas. No crear ventas de prueba en esa base sin autorización
   específica: el recorrido completo debe ejecutarse en la base temporal.

Las migraciones existentes ya fueron aplicadas a `elcarbonazo`, incluida la de
anulaciones por autorización explícita del usuario; esa función permanece pausada.
No modificar migraciones anteriores ni eliminar el pedido abierto.
