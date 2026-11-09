# Evolución futura por módulos — El Carbonazo

## Propósito y alcance

Este documento reúne ideas, decisiones pendientes y trabajo que podría llevar el prototipo hacia una instalación utilizable en el restaurante. Es un **inventario de planificación**, no una especificación aprobada, un calendario ni una afirmación de que esas funcionalidades ya existen. Se actualizará conforme se definan nuevas necesidades.

El proyecto universitario sigue siendo un **prototipo funcional local de autenticación y mesas**. Eso no justifica una implementación descuidada: dentro de su alcance debe mantener reglas correctas, una arquitectura clara, pruebas proporcionales, manejo honesto de errores y una interfaz legible y cuidada. La diferencia es que aún no se declara listo para operar el negocio ni se exige completar todos los módulos futuros para presentarlo.

### Cómo interpretar las categorías

- **Base existente:** funcionalidad o infraestructura ya implementada dentro del alcance conocido. Puede requerir revisión y mejoras; no implica aceptación para producción.
- **Pendiente para despliegue:** trabajo o verificación necesarios antes de considerar el uso real, sujeto a evaluación del contexto del restaurante.
- **Por definir:** idea o pregunta de negocio que aún requiere decisión y especificación. No se debe presentar como comportamiento disponible.

Las referencias autoritativas siguen siendo `docs/constitution.md`, `docs/domain.md` y las especificaciones aprobadas en `specs/`. Si este inventario discrepa con ellas, prevalecen esas fuentes. Antes de implementar un módulo o cambiar reglas: acordar alcance y criterios, preparar o actualizar `spec.md`, `plan.md` y `tasks.md`, aprobarlos y después desarrollar y probar. No agregar módulos ficticios a la interfaz para anticipar el trabajo.

## 1. Presentación e instalación local del MVP

**Base existente:** API ASP.NET Core, Desktop Avalonia/SukiUI, PostgreSQL, autenticación, mapa de mesas y pruebas automatizadas. T5/T6 se aceptaron para el perfil universitario con límites documentados en `docs/validacion-t5.md` y `docs/validacion-t6.md`.

**Mejoras útiles para presentación y continuidad:**

- Preparar una guía breve y reproducible para instalar requisitos, configurar el entorno local, aplicar migraciones e iniciar API y Desktop en una laptop nueva.
- Mantener datos de demostración aislados y claramente identificados; no usar credenciales reales ni alterar datos existentes para hacer pruebas.
- Documentar un recorrido de demostración y validar en el equipo que se utilizará, incluidos arranque, login, cambios de mesa, agrupación, desconexión y salida.
- Mantener una descripción de lo que está implementado, de las limitaciones conocidas y de cómo ejecutar las pruebas. No sustituir la validación funcional por capturas o datos inventados.

## 2. Identidad, empleados y sesiones

**Base existente:** login, roles, JWT y ciclo de sesión del módulo 001; bootstrap inicial de Jefa y seeder exclusivo de Development. El comportamiento y las verificaciones están definidos en el módulo 001.

**Por definir:**

- Administración de empleados: alta, modificación, asignación de roles, desactivación y restablecimiento de contraseña; responsables autorizados, confirmaciones, auditoría y tratamiento de sesiones activas.
- Flujo de recuperación de acceso y procedimiento cuando se pierde el acceso administrativo, sin introducir accesos secretos ni depender de SQL manual para la operación cotidiana.
- Necesidades reales de permisos por rol y separación de responsabilidades. Los roles y permisos actuales permanecen vigentes mientras no se apruebe un cambio.

**Pendiente para despliegue:** verificar la configuración y protección de credenciales, tiempos de sesión, bloqueo, revocación y el bootstrap en el entorno real; revisar visualmente avisos de sesión y error TLS. Evitar usar el usuario de Development en producción.

## 3. Mesas, reservas y agrupación

**Base existente:** 15 mesas, estados `Libre`, `Ocupada`, `Reservada` y `Agrupada`, transiciones autorizadas, selección, cambios individuales, creación y cierre de grupos, polling y controles de concurrencia del módulo 001. `Agrupada` indica pertenencia a un grupo activo; por sí sola no confirma que haya clientes ni un pedido abierto.

**Por definir:**

- Gestión funcional de reservas: datos necesarios, horario, responsable, llegada, cancelación y vencimiento. El estado actual `Reservada` no equivale por sí solo a un módulo completo de reservas.
- Si deben poder agruparse mesas reservadas y bajo qué condiciones: por ejemplo, cómo comprobar que pertenecen a una misma reserva y qué sucede al cancelar o cerrar el grupo. **Regla vigente:** solo se agrupan mesas `Libre`, sin grupo y sin pedidos pendientes.
- Necesidades de navegación, búsqueda o indicadores adicionales basados únicamente en información real disponible por API.

**Pendiente para despliegue:** probar concurrencia y recuperación en terminales reales; medir de forma cronometrada el tiempo desde la confirmación hasta el render y la cadencia en la red de destino. El número de mesas físicas sigue siendo 15 mientras no se apruebe otro alcance.

## 4. Operaciones recuperables ante resultado incierto

**Base existente:** el prototipo no reintenta automáticamente escrituras inciertas; informa, consulta y solicita revisión humana. **Un GET no demuestra que la escritura anterior haya terminado**, por lo que este mecanismo no garantiza recuperación definitiva y no debe considerarse suficiente para operación real.

**Pendiente antes de operación real:** revisar y aprobar la propuesta pospuesta [`specs/002-operaciones-seguras/`](../specs/002-operaciones-seguras/). Su diseño propone identificación de intenciones, preparación/ejecución, resultados persistentes, resolución o cancelación supervisada y un registro local seguro. Aún **no está implementada ni aprobada para despliegue**; requiere validación de carreras, auditoría, compatibilidad, migraciones y recuperación tras reinicio. No habilitar reintentos automáticos equivalentes por interpretar un GET como confirmación.

## 5. Pedidos y atención de salón

**Base existente:** el dominio y la API 003 permiten consultar catálogo/cuentas y registrar envíos idempotentes, además de comprobar cuentas pendientes para las reglas de mesas/grupos. Eso **no significa que exista un flujo completo de pedidos en Desktop ni un sistema listo para operación real**.

**Módulo 003 en curso:** [`specs/003-pedidos-kds/`](../specs/003-pedidos-kds/) define cuenta por destino, envíos separados, catálogo de demostración y cocina. D1 y API de D2/D3 están implementados con pruebas automatizadas; la migración no se ha aplicado a una base persistente. D4 incluye pantalla de salón y envío conectados, con pruebas focalizadas; aún faltan validación integral y revisión visual. Hay casos Unicode compartidos de API y Desktop. La pantalla de cocina sigue pendiente; la recuperación probada es controlada, no una aceptación de red física.

**Por definir:**

- El envío idempotente y la apertura transaccional están implementados en API; Desktop incorpora borrador local, presentación de estados y envío, pendientes de validación integral y visual.
- Cancelación, edición de envíos, pagos y cierre permanecen fuera de 003 y requieren especificación posterior.
- La API respeta el orden de locks documentado y se probó frente a carreras principales en PostgreSQL temporal; las nuevas rutas deberán conservarlo.
- Ante resultado incierto no se reintenta automáticamente ni se usa un GET como prueba de terminación; el alcance y límite tras reinicio están documentados en 003.

## 6. Cocina

**Base existente:** el rol `Personal de Cocina` puede consultar mesas y no modificarlas. El módulo 001 no implementa una pantalla operativa de cocina.

**003 en curso:** la API de cola FIFO y transición de comandas está implementada y probada; Cocina opera estados sin modificar mesas. La matriz incluye Jefa/Supervisor en KDS y permite a Mesero/Caja consultar estados desde la cuenta. La pantalla Desktop de cocina y el polling siguen pendientes.

La especificación 003 define recepción, orden FIFO, estados, notas, roles y comunicación visible en salón. La implementación y validación del polling, volumen, accesibilidad y desconexión siguen pendientes; no se garantizan notificaciones instantáneas.

## 7. Productos, menú y precios

**Base de dominio:** `docs/domain.md` describe productos y precios; una entidad descrita en el diccionario no implica pantallas o gestión implementadas.

003 implementa en API catálogo demostrativo, categorías, disponibilidad, precios históricos y revalidación de versión; no incluye administración de menú ni cambio de precios por usuarios. La gestión real del menú, permisos, correcciones e historial de cambios requieren especificación posterior.

## 8. Inventario, ingredientes y recetas

**Base de dominio:** el diccionario describe ingredientes, recetas y sus relaciones; no se declara implementada una operación completa de inventario.

En la primera entrega propuesta de 003, `disponible` describe solo el catálogo; no hay comprobación, reserva ni reversión de ingredientes. Integrar inventario exige una especificación posterior y no debe simularse.

**Por definir:** unidades, entradas y salidas, ajustes autorizados, recetas, consumos, mermas, alertas y trazabilidad. Acordar si el inventario cambia al preparar, vender o mediante otro evento, y cómo se revierten o corrigen movimientos. Requiere especificación y pruebas transaccionales propias.

## 9. Caja, pagos, facturación y moneda

**Reglas de referencia generales:** importes en NIO, posible operación bimonetaria NIO/USD y referencias preliminares a IVA 15 %/propina 10 % en el modelo de dominio. No son por sí solas una regla fiscal validada ni se aplican automáticamente al restaurante. **Para el negocio actual**, según información del usuario sobre su régimen de cuota fija, 003 no añade IVA y los pedidos no son facturas. Cualquier módulo futuro de caja debe confirmar con el negocio/asesor competente el régimen, precios, documentos e impuestos vigentes antes de especificar o calcular cobros.

**Por definir:**

- Apertura y cierre de caja, responsables, medios de pago, pagos mixtos, anulación y conciliación.
- Aplicación y vigencia de la tasa de cambio, redondeo, moneda de presentación y conservación de la tasa utilizada en cada factura.
- Si corresponde algún impuesto o propina, su momento/base de cálculo, documentos, correcciones y reportes, de acuerdo con requisitos legales y comerciales vigentes confirmados con el negocio; no asumir que aplica el IVA general del 15 %.
- Cómo se relaciona el pago con el pedido, la cuenta de grupo y la liberación explícita de mesas; pruebas con importes, concurrencia y fallos.

No asumir aprobación fiscal o contable solo por existir reglas de dominio preliminares.

## 10. Reportes y auditoría

**Base existente:** auditoría persistente de eventos críticos y logs técnicos; en el módulo 001 no hay consulta de auditoría por API o interfaz. Las retenciones y restricciones existentes están descritas en `docs/domain.md`.

**Por definir:** qué reportes necesita cada rol, períodos, filtros, exportación local y conciliación con ventas, caja e inventario. Si se requiere consultar auditoría desde la aplicación, definir acceso mínimo, protección de datos, paginación y trazabilidad de las consultas antes de implementarlo.

**Pendiente para despliegue:** comprobar permisos append-only y retención, procedimientos de archivo externo y que los registros no contengan contraseñas, tokens ni otros secretos.

## 11. Interfaz, accesibilidad y experiencia

**Base existente:** dirección «Carbón & Fuego», tarjetas rectangulares de mesas, detalle y acciones. Parte de la revisión visual fue realizada por el usuario; la evidencia y sus límites están en `docs/validacion-t5.md` y `docs/validacion-t6.md`.

**Mejora continua:** pulir jerarquía visual, espaciado, botones, mensajes y coherencia entre pantallas; reutilizar recursos y mantener únicamente navegación funcional. Mantener MVVM, bindings y code-behind conforme a la arquitectura acordada, sin cambios de paquetes no probados.

**Pendiente para despliegue:** revisión visual completa con escala y tamaños de ventana reales, contraste de estados y controles, teclado/foco, nombres accesibles y avisos de sesión/TLS. Confirmar decisiones de diseño con el usuario; no depender solo del color ni sacrificar legibilidad por animaciones.

## 12. Infraestructura, instalación y operación LAN

**Base existente:** hay configuración, plantillas y documentación de referencia; la tarea **T3.10 sigue pendiente** y no debe interpretarse como despliegue LAN aceptado. Ver `docs/deployment-lan.md` y `docs/installation.md`.

**Pendiente para despliegue:**

- Preparar servidor, PostgreSQL y terminales con configuración externa y sin secretos versionados; separar credenciales administrativas de migración y rol runtime sin DDL.
- Configurar hostname LAN, HTTPS con certificado confiable, firewall y almacenes de certificados; nunca desactivar la validación TLS. Provisionar identidad de instalación y permisos de configuración/logs.
- Probar desde una segunda terminal en la red prevista, sin Internet, incluyendo caídas, recuperación, roles y rendimiento observable.
- Definir instalación, actualización, compatibilidad de versiones, migraciones y reversión controlada con respaldo previo; no sobrescribir configuración o datos existentes silenciosamente.
- Poner en práctica respaldo diario cifrado y restauración periódica en una base aislada; documentar responsables, monitoreo, almacenamiento separado y recuperación ante fallos.

La ejecución local de demostración y el anfitrión temporal de pruebas no sustituyen estas comprobaciones de despliegue.

## 13. Calidad, seguridad y preparación para producción

**Base existente:** pruebas automatizadas API/Desktop y aislamiento Desktop→REST; T5 y T6 fueron aceptadas para el perfil universitario con límites explícitos, no como certificación para producción.

**Pendiente para despliegue:** medir y alcanzar el objetivo de cobertura ≥80 % en lógica crítica según el alcance que se apruebe; ampliar pruebas de integración, concurrencia, recuperación, sesión y fallos. Completar el checklist visual y de accesibilidad, comprobar dependencias y vulnerabilidades, revisar logs y secretos, y repetir la secuencia de formato, build, pruebas API, pruebas Desktop y auditoría de paquetes.

Revisar la capacidad real de los equipos, la observabilidad y procedimientos de soporte, sin convertir estimaciones de laboratorio en garantías de servicio. La fecha y los resultados de cada aceptación deben quedar registrados.

## 14. Organización sugerida del trabajo

El orden definitivo se acordará según necesidades y dependencias, no por esta lista. Una secuencia razonable para evaluar es:

1. Mantener estable y demostrable el módulo 001; registrar defectos y mejoras visuales reales.
2. Definir empleados y prioridades de operación; especificar los módulos de productos y pedidos antes de integrarlos en la navegación.
3. Definir cocina, caja/facturación e inventario con sus reglas y pruebas, considerando sus dependencias de pedidos y productos.
4. Revisar operaciones recuperables 002 y completar seguridad, infraestructura y aceptación física antes de operar con datos reales.
5. Validar reportes, respaldo/restauración, formación y procedimientos operativos con el negocio.

Estas etapas pueden revisarse; no autorizan implementar módulos, cambiar reglas fiscales o declarar listo el sistema sin especificaciones y aceptación correspondientes.
