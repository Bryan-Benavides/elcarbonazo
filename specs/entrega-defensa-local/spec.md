# Cierre y entrega reproducible de la demo local

**Estado: alcance de entrega actualizado por solicitud del usuario. El usuario pidió preparar catálogo local de defensa con los 90 productos, recetas e ingredientes suministrados, probarlo y después entregar ejecutables y una base PostgreSQL local restaurable. Prefiere PostgreSQL instalado directamente en Windows, sin Docker ni nube. El 006 anterior de anulaciones queda pausado.**

## Objetivo

Reducir el trabajo final a probar los flujos ya aceptados en una instalación limpia y después entregar una demostración reproducible en laptops Windows: ejecutables/paquete portable y una base local de defensa precargada y restaurable. El resultado no es un despliegue productivo.

## Alcance propuesto

- No añadir funciones fuera de reservas 006 y reporte 007 definidos en sus especificaciones.
- Posponer el 006 anterior de auditoría/anulaciones. No borrar ni revertir cambios locales existentes como parte de esta pausa.
- **Fase 1 — preparación y pruebas:** el usuario designó y autorizó expresamente la base PostgreSQL local existente `elcarbonazo` como destino de defensa, tras confirmar que hizo un respaldo. Se autoriza insertar el catálogo nuevo únicamente si las tablas de catálogo e inventario están vacías; se conservan usuarios, mesas, pedidos y el resto del historial. No sobrescribir registros existentes ni utilizar el entorno temporal de pruebas como destino final.
- Usar migraciones existentes y cargar explícitamente 15 mesas, el catálogo de 90 productos, recetas y stock inicial proporcionados por el usuario. No usar nombres de productos artificialmente ficticios ni inventar cantidades ausentes. La carga valida el conjunto antes de escribir y no sobrescribe datos existentes.
- Las fuentes versionadas son `catalogo-productos.csv`, `catalogo-ingredientes.csv` y `recetas.txt` en este directorio. Cada archivo debe validar cantidad de filas, claves únicas, categoría, precio, unidad y referencias antes de importarse. `scripts/validar-catalogo-defensa.py` ejecuta esas comprobaciones sin acceso a PostgreSQL; no sustituye la verificación de transcripción ni prueba una importación.
- Probar sobre esa base aislada la secuencia salón → reservación → pedido → Cocina → Caja y comprobar que inventario no se descuenta dos veces; validar el reporte de ventas y ejecutar regresión API/Desktop. No publicar paquete final antes de superar esta fase.
- **Fase 2 — entrega:** publicar ejecutables de Windows y un dump restaurable creado exclusivamente desde la base local de defensa aislada; no usar un backup de una base preexistente.
- Documentar requisitos, configuración local no secreta, restauración de la base, comandos para iniciar/detener, credenciales claramente marcadas como demo y resolución de errores frecuentes.

## Fuera de alcance

Operación real, LAN física, TLS/certificados para producción, usuarios reales, restauración de la base original, completar cancelaciones 006/002, facturación/IVA, ampliar módulos, instalador MSI, cobertura objetivo de producción y pulido cosmético no necesario para el recorrido.

## Criterios de aceptación

1. La fase 1 crea una base local de defensa por nombre explícito, aislada de toda base preexistente, con el catálogo/recetas/stock validados; seed/reinicio no duplican ni mutan destinos ajenos.
2. Pasan formato, build, suites API/Desktop y smoke: login, reserva, pedido, KDS, Caja/comprobante, reporte e inventario sin doble consumo; los 90 productos tienen receta activa compatible y stock suficiente para el guion de defensa.
3. Solo tras el paso 2, la fase 2 produce ejecutables Windows y un dump restaurable de la misma base local de defensa, sin contraseñas personales ni secretos operativos.
4. Un compañero puede seguir la guía para restaurar la base y ejecutar la demo en otra laptop; se registra la versión requerida de PostgreSQL y cualquier parte no probada.
5. La aplicación se presenta como universitaria/local, no como sistema de producción. No se ocultan los límites de pagos externos ni el carácter no fiscal de los comprobantes.
