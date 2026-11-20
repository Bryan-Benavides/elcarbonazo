# Matriz de pruebas ejecutadas y evidencias de validación

**Universidad:** Universidad Nacional de Ingeniería  
**Asignatura:** Ingeniería de Software I  
**Sistema:** El Carbonazo — punto de venta para restaurante  
**Integrantes:** [escribir nombres de los tres integrantes]  
**Fecha de entrega:** [completar]

## 1. Objetivo

Presentar los resultados de las pruebas realizadas y relacionar las capturas de pantalla del sistema con sus principales requerimientos. La validación corresponde al alcance académico local; las pruebas automáticas no sustituyen la revisión visual ni demuestran operación comercial o conexión entre computadoras en una red física.

## 2. Entorno y forma de validación

El sistema está compuesto por una API ASP.NET Core, una aplicación de escritorio Avalonia y una base de datos PostgreSQL. Las pruebas de integración indicadas como **automáticas** utilizaron bases PostgreSQL temporales con datos de prueba. Las verificaciones **manuales** corresponden a lo que el usuario confirmó haber probado en pantalla.

En la tabla, **aprobado** significa que el caso descrito fue observado o pasó la prueba indicada; no implica que se hayan comprobado todas las situaciones posibles del módulo.

## 3. Relación de requerimientos

| Código | Requerimiento funcional |
|---|---|
| RF-01 | Permitir el acceso mediante usuario y contraseña. |
| RF-02 | Restringir las funciones disponibles según el rol del usuario. |
| RF-03 | Mostrar las mesas y sus estados; permitir su administración según los permisos. |
| RF-04 | Registrar un pedido y enviarlo a Cocina. |
| RF-05 | Mostrar las comandas en Cocina y actualizar su estado. |
| RF-06 | Abrir y cerrar caja, registrar cobros y consultar comprobantes internos. |
| RF-07 | Gestionar ingredientes, existencias y recetas; descontar inventario al enviar un pedido. |
| RF-08 | Registrar y consultar reservaciones de mesas. |
| RF-09 | Mostrar un reporte de ventas a usuarios autorizados. |
| RF-10 | Anular líneas o comandas con autorización superior y conservar el registro. |
| RF-11 | Permitir continuar la atención con un nuevo pedido después de una anulación total, sin liberar la mesa ni deshacer el grupo. |

## 4. Matriz de pruebas ejecutadas

| ID | Requisito | Caso ejecutado | Resultado esperado | Resultado observado y evidencia | Estado |
|---|---|---|---|---|---|
| CP-01 | RF-01, RF-02 | Inicio de sesión con los usuarios Jefa, Cocina y Mesero. | Cada usuario inicia sesión y accede según su rol. | El usuario confirmó que inició sesión con las tres cuentas y revisó sus permisos. | Aprobado manualmente |
| CP-02 | RF-03, RF-04 | Enviar pedidos para una mesa individual y un grupo. | Se crea la cuenta correspondiente y cada envío genera una comanda. | Recorrido `--caja-smoke`: envío de pedidos para mesa y grupo en base temporal. | Aprobado automáticamente |
| CP-03 | RF-05 | Pasar comandas por Cocina hasta el estado de lista. | Las comandas aparecen en la cola y pueden avanzar de estado. | El recorrido `--caja-smoke` verificó las transiciones de Cocina mediante API. | Aprobado automáticamente |
| CP-04 | RF-06 | Abrir turno, registrar cobros y cerrar caja. | Las operaciones se registran y el comprobante corresponde al cobro. | `--caja-smoke` comprobó apertura, cobros, comprobantes, recuperación y cierre con datos ficticios. | Aprobado automáticamente |
| CP-05 | RF-07 | Enviar pedido con receta y comprobar consumo. | El envío descuenta inventario y el cobro no vuelve a descontarlo. | `--catalogo-defensa-smoke` verificó el consumo del envío y la ausencia de doble descuento en base temporal. | Aprobado automáticamente |
| CP-06 | RF-08 | Registrar una reservación y cancelarla. | La reserva aparece en el mapa y puede cancelarse. | `--caja-smoke` verificó reserva, consulta en mapa y cancelación por API. | Aprobado automáticamente |
| CP-07 | RF-09 | Consultar el reporte de ventas. | Los totales y cantidades coinciden con los cobros registrados. | `--caja-smoke` comparó los agregados del reporte con los cobros de prueba. | Aprobado automáticamente |
| CP-08 | RF-10 | Anular una línea o comanda con autorización superior. | Se registra la anulación y se actualiza la cuenta. | El usuario confirmó que probó visualmente la anulación y que funciona. `--anulaciones-smoke` también verificó autorización, ejecución, consulta y barrera en base temporal. | Aprobado en el recorrido informado |
| CP-09 | RF-10, RF-11 | Tras una anulación total, enviar otro pedido a la misma mesa. | El pedido anterior permanece cancelado y se crea una cuenta nueva sin liberar la mesa; el nuevo consumo se registra una vez. | `AnulacionEjecucionTests`: prueba en PostgreSQL temporal, con verificación de cuentas, indicador y stock. | Aprobado automáticamente |
| CP-10 | RF-10, RF-11 | Tras una anulación total, enviar otro pedido a un grupo de mesas. | El grupo permanece unido y puede recibir un nuevo pedido; se conserva el historial. | `Grupo_permite_nuevo_pedido_tras_anulacion_total_y_conserva_historial`, en PostgreSQL temporal. | Aprobado automáticamente |
| CP-11 | RF-10 | Cerrar el cuadro de anulación sin confirmar. | El cuadro se cierra y no se envía ninguna autorización. | `Opening_and_cancelling_modal_notifies_button_states_without_sending`, prueba del ViewModel. | Aprobado automáticamente; captura pendiente |

### Resultados generales de ejecución

- La compilación de la solución se completó sin errores ni advertencias en la revisión registrada.
- La suite Desktop registró **289 pruebas aprobadas** en la ejecución documentada, antes de las modificaciones posteriores de continuidad de pedido.
- Las **18 pruebas API focalizadas** de anulaciones, caja, inventario y persistencia seleccionadas tras el cambio de continuidad pasaron en bases temporales.
- Los recorridos `--anulaciones-smoke`, `--caja-smoke` y `--catalogo-defensa-smoke` pasaron en bases temporales. Estos recorridos no validan visualmente las ventanas ni una red física.

**Fuentes de verificación:** resultados de ejecución registrados durante el desarrollo; `tests/ElCarbonazo.Api.Tests/AnulacionEjecucionTests.cs`; `tests/ElCarbonazo.Desktop.Tests/PedidosSalonAnulacionTests.cs`; `tests/ElCarbonazo.AcceptanceHost`; y `docs/validacion-006.md`.

## 5. Evidencias del sistema desarrollado

**Instrucción para completar:** inserte una captura real debajo de cada título. Si una funcionalidad no se captura o no se prueba, deje constancia de ello; no sustituya una imagen con una captura de otro módulo. Los cuadros entre corchetes son espacios pendientes, no evidencias ya obtenidas.

### Figura 1. Inicio de sesión

**Requerimiento:** RF-01.  
**Propósito:** muestra la pantalla donde un usuario ingresa sus credenciales para acceder al sistema.  
**Captura:** [Insertar imagen de la pantalla de inicio de sesión, sin contraseña visible].

### Figura 2. Acceso según rol

**Requerimiento:** RF-02.  
**Propósito:** muestra las opciones disponibles para uno de los roles de prueba. Puede complementarse con una segunda captura de otro rol para comparar los accesos.  
**Captura:** [Insertar imagen del menú de Jefa, Cocina o Mesero e indicar el rol].

### Figura 3. Mapa y estados de mesas

**Requerimiento:** RF-03.  
**Propósito:** presenta las mesas identificadas por número y su estado, lo que permite al personal reconocer la disponibilidad.  
**Captura:** [Insertar imagen del mapa de mesas].

### Figura 4. Registro de pedido de salón

**Requerimiento:** RF-04.  
**Propósito:** muestra el catálogo, el borrador y la cuenta confirmada de una mesa; permite explicar cómo se prepara y envía una comanda.  
**Captura:** [Insertar imagen de Pedidos de salón con un producto y la cuenta correspondiente].

### Figura 5. Cola de comandas de Cocina

**Requerimiento:** RF-05.  
**Propósito:** muestra las comandas recibidas y sus estados de preparación.  
**Captura:** [Insertar imagen de Cocina con una comanda visible].

### Figura 6. Caja y cobro de una cuenta

**Requerimiento:** RF-06.  
**Propósito:** muestra el turno de caja o la pantalla utilizada para seleccionar y cobrar una cuenta.  
**Captura:** [Insertar imagen de Caja con el turno y la cuenta; ocultar referencias de pago que no deban compartirse].

### Figura 7. Comprobante interno de cobro

**Requerimiento:** RF-06.  
**Propósito:** muestra el detalle histórico del cobro y los productos registrados. El comprobante es interno y no constituye factura fiscal.  
**Captura:** [Insertar imagen del comprobante interno].

### Figura 8. Inventario y existencias

**Requerimiento:** RF-07.  
**Propósito:** presenta los ingredientes y sus cantidades disponibles para la preparación de productos.  
**Captura:** [Insertar imagen de Inventario con ingredientes y existencias].

### Figura 9. Recetas de productos

**Requerimiento:** RF-07.  
**Propósito:** muestra la relación entre un producto y los ingredientes que se consumen al enviar su comanda.  
**Captura:** [Insertar imagen de una receta activa y sus ingredientes].

### Figura 10. Reservaciones de mesas

**Requerimiento:** RF-08.  
**Propósito:** muestra la agenda o el registro de una reservación para una mesa y una franja de tiempo.  
**Captura:** [Insertar imagen de la pantalla de reservaciones].

### Figura 11. Reporte de ventas

**Requerimiento:** RF-09.  
**Propósito:** presenta un resumen de los cobros registrados para el período seleccionado.  
**Captura:** [Insertar imagen del reporte de ventas].

### Figura 12. Autorización de anulación

**Requerimiento:** RF-10.  
**Propósito:** muestra la solicitud de un motivo y la validación de la Jefa antes de anular una línea o comanda.  
**Captura:** [Insertar imagen del cuadro de autorización; no mostrar contraseña ni información sensible].

### Figura 13. Resultado de la anulación y aviso a Cocina

**Requerimiento:** RF-10.  
**Propósito:** evidencia que el producto anulado deja de formar parte activa de la cuenta y que Cocina recibe el aviso correspondiente. Si es necesario, utilice dos capturas: una de la cuenta y otra de la alerta.  
**Captura:** [Insertar imagen de la cuenta actualizada y/o de la alerta de Cocina].

### Figura 14. Nuevo pedido después de una anulación

**Requerimiento:** RF-11.  
**Propósito:** muestra que, después de anular completamente un pedido, se puede registrar otro sin liberar la mesa o desagrupar las mesas.  
**Captura:** [Insertar imagen del nuevo pedido y, de ser posible, una imagen previa del estado posterior a la anulación].

## 6. Conclusión

Las pruebas registradas muestran el funcionamiento de los principales flujos del sistema en el alcance académico local. Las capturas anteriores deben completarse con imágenes obtenidas durante la ejecución real para respaldar visualmente cada funcionalidad.

**Limitaciones:** las pruebas automáticas en bases temporales no demuestran por sí solas la apariencia visual, el funcionamiento en una red física ni la instalación en otra computadora. El sistema no verifica pagos bancarios y sus comprobantes no son facturas fiscales.
