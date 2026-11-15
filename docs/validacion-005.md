# Validación observada — 005 Inventario y escandallo

## Persistencia y API (C1–C2)

Las pruebas usan bases PostgreSQL temporales `elcarbonazo_test_<uuid>`. La migración se comprobó con historial representativo de 003/004, sin fabricar consumos, y su reversión automática queda bloqueada. No se ha aplicado a una base persistente de demostración por estas pruebas.

Se verificaron validaciones y permisos HTTP esenciales, movimientos y auditoría, agregación decimal, rechazo por stock o receta, rollback ante fallo de auditoría y reproducción del mismo envío sin duplicar consumo. Dos envíos compitieron por el último stock; una prueba simultánea no demuestra todas las intercalaciones. Pruebas con bloqueo observado en PostgreSQL cubrieron ambos órdenes de reemplazo de receta/envío y creación de receta/inactivación; otra comprobó inactivación esperando a envío y rechazo de versión obsoleta.

Pasaron formato, build, 141 pruebas API, 232 Desktop y auditoría de paquetes sin vulnerabilidades reportadas en los orígenes consultados. `AcceptanceHost --pedidos-smoke` y `--caja-smoke` pasaron con recetas y existencias ficticias en bases temporales. El segundo verificó consumo por envío y ausencia de un segundo descuento al cobrar. Usa REST y ViewModel con sesión/diario adaptados: no acredita render, ACL reales ni LAN.

## Pendiente

Pedidos interpreta el rechazo de stock y muestra faltantes solo si sus datos son coherentes. La vista permite filtrar ingredientes, consultar kardex y, para Jefa, crear/editar ingredientes, registrar movimientos y editar recetas. La suite Desktop pasó 256 pruebas. `AcceptanceHost --inventario-smoke` ejerció creación, entrada, edición, kardex y guardado de receta mediante ViewModel, REST y PostgreSQL temporal, con sesión adaptada; no abrió ventana ni probó render o teclado. Faltan buscador de productos, ampliar pruebas de formularios/errores y aceptación visual local. No se afirma una matriz exhaustiva de fallos, recuperación durable administrativa, aptitud de producción o uso con dinero real.
