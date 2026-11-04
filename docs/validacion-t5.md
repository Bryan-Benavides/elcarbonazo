# Validación T5 — mapa de mesas

Estado: **pendiente de aceptación funcional/visual y medición de convergencia**. Este registro no convierte el prototipo en apto para producción.

## Evidencia automatizada

El 4 de noviembre de 2026 se ejecutaron las pruebas existentes contra una base PostgreSQL temporal aislada y pruebas Desktop con servicios simulados. La API verificó, entre otros, el contrato HTTP de 15 mesas, cambios de estado y grupos. Estas pruebas no demuestran el comportamiento visual ni sustituyen una prueba Desktop conectada a la API.

Contraste calculado para texto y fondos del sistema visual (luminancia relativa WCAG): `#F3F4F6` sobre `#1E1E1E` 15,15:1 y sobre `#2D2D30` 12,48:1; `#9CA3AF` sobre esos fondos 6,57:1 y 5,41:1, respectivamente. No sustituye revisar estados interactivos, escalado y render reales.

## Aceptación pendiente con la aplicación

Usar una instalación de pruebas aislada, nunca datos reales de operación. Registrar entorno, fecha y resultado:

- [ ] Autenticarse y comprobar las 15 mesas, números, capacidades, estados y colores con texto legible.
- [ ] Para los roles autorizados, probar reserva, ocupación y liberación con cancelación y confirmación; verificar rechazo por cuenta pendiente mediante datos de prueba controlados.
- [ ] Comprobar que Personal de Cocina solo puede consultar.
- [ ] Seleccionar mesas libres, crear un grupo, comprobar sus miembros y cerrar con confirmación; verificar el rechazo ante cuenta compartida pendiente.
- [ ] Comprobar conflicto entre dos sesiones de prueba, desconexión y recuperación, y los avisos de sesión pertinentes sin repetir escrituras.
- [ ] Revisar panel de detalle, acciones y confirmaciones a tamaño mínimo 800×560 y a un tamaño mayor; verificar desplazamiento, foco y contraste. El efecto al pasar el mouse sobre tarjetas ya fue confirmado por el usuario.
- [ ] Cerrar sesión y comprobar retorno a login.

## Medición de convergencia pendiente

Con API y Desktop saludables en el entorno de prueba, realizar cambios desde una segunda sesión y registrar el instante en que la API confirma cada cambio y el instante en que la tarjeta de la primera sesión lo muestra renderizado. Usar una referencia de tiempo común o una grabación con cronómetro visible; anotar resolución del método, número de muestras, mínimo, máximo y condiciones (equipo, red y versión). Registrar también duración del GET y espera cuando estén disponibles. No confundir recepción HTTP o actualización del ViewModel con render en pantalla.

Comprobar la cadencia nominal de inicio de GET de 2 segundos sin solapamiento y documentar cualquier desviación. El objetivo nominal incluye espera de polling, duración del GET y despacho/render; **no es una garantía absoluta de 2 segundos**. No hay medición real registrada todavía.
