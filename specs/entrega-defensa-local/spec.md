# Cierre y entrega reproducible de la demo local

**Estado: propuesta de alcance. El usuario pidió completar reservas sencillas y reporte de ventas, probarlos y después entregar ejecutables y una base demo distribuible. Prefiere PostgreSQL instalado directamente en Windows, sin Docker ni nube para la defensa. El 006 anterior de anulaciones queda pausado; pendiente aprobación de los criterios concretos de reservas, reporte y entrega.**

## Objetivo

Reducir el trabajo final a probar los flujos ya aceptados en una instalación limpia y después entregar una demo reproducible en laptops Windows: ejecutables/paquete portable y una base demo precargada distribuible. El resultado no es un despliegue productivo.

## Alcance propuesto

- No añadir funciones fuera de reservas 006 y reporte 007 definidos en sus especificaciones.
- Posponer el 006 anterior de auditoría/anulaciones. No borrar ni revertir cambios locales existentes como parte de esta pausa.
- **Fase 1 — pruebas:** crear una base PostgreSQL demo nueva y aislada en una instalación local. Nunca copiar, migrar, sobrescribir ni sembrar la base original del usuario.
- Usar migraciones existentes y datos demo deterministas suficientes para presentar 15 mesas, catálogo disponible y recetas/stock de demostración si son necesarios para los flujos ya implementados.
- Probar sobre esa base aislada la secuencia salón → reservación → pedido → Cocina → Caja y comprobar que inventario no se descuenta dos veces; validar el reporte de ventas y ejecutar regresión API/Desktop. No publicar paquete final antes de superar esta fase.
- **Fase 2 — entrega:** publicar ejecutables de Windows y un dump restaurable creado exclusivamente desde la base demo sintética aprobada; no usar un backup de la base personal/original.
- Documentar requisitos, configuración local no secreta, restauración de la base, comandos para iniciar/detener, credenciales claramente marcadas como demo y resolución de errores frecuentes.

## Fuera de alcance

Operación real, LAN física, TLS/certificados para producción, usuarios reales, restauración de la base original, completar cancelaciones 006/002, facturación/IVA, ampliar módulos, instalador MSI, cobertura objetivo de producción y pulido cosmético no necesario para el recorrido.

## Criterios de aceptación

1. La fase 1 crea una base demo por nombre explícito, aislada de la original y con datos ficticios suficientes; seed/reinicio no duplican ni mutan destinos ajenos.
2. Pasan formato, build, suites API/Desktop y smoke: login, reserva, pedido, KDS, Caja/comprobante, reporte e inventario sin doble consumo.
3. Solo tras el paso 2, la fase 2 produce ejecutables Windows y un dump restaurable de la misma base demo sintética, sin contraseñas personales ni secretos operativos.
4. Un compañero puede seguir la guía para restaurar la base y ejecutar la demo en otra laptop; se registra la versión requerida de PostgreSQL y cualquier parte no probada.
5. La demo se presenta como universitaria/local, no como sistema de producción.
