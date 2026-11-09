# Validación D5 — Cocina Desktop

## Estado

**D5 aceptada por el usuario para el perfil universitario local**, según revisión visual y funcional reportada después de ejecutar cada punto de este documento. No es aceptación para operación real ni red LAN física. El usuario confirmó que las comandas aparecen, avanzan por preparación a lista y desaparecen de la cola al completarse; en la cuenta de salón permanecen visibles con estado listo. Se ajustó después la jerarquía visual del acceso Cocina en la navegación desplegable para igualarla a la entrada activa de Mesas; ese detalle requiere validar la nueva compilación en pantalla.

La vista permite abrir Cocina desde la navegación del mapa a Jefa, Supervisor y Personal de Cocina; la API mantiene la autorización como autoridad. Muestra como máximo las primeras 100 comandas FIFO, con aviso si hay más; las siguientes aparecen al avanzar las anteriores. La paginación no garantiza snapshot entre páginas. Las horas se muestran en UTC explícitamente.

La cola muestra destino, fecha, estado textual, productos, cantidades y notas. Las únicas transiciones son `Abierta` → `En Preparación` → `Lista`; lista no equivale a retirada ni pago. El polling es secuencial y se coordina con la escritura. Ante conflicto se exige nueva decisión después de actualizar. Ante respuesta incierta no se reintenta: se bloquean acciones y regreso durante la vida de la vista; una consulta no prueba que la escritura terminó. Cerrar la aplicación o terminar sesión pierde este bloqueo local y requiere revisión humana; no proporciona recuperación durable ni protege otras terminales.

## Evidencia automatizada

El 9 de noviembre de 2026 pasaron formato de solución y AcceptanceHost, compilación, 50 pruebas API y 141 Desktop, y la auditoría no reportó paquetes vulnerables. Las pruebas de Cocina cubren DTO/REST, transiciones, cola vacía/fallo, conflicto, sesión pausada, lectura previa a escritura, roles de navegación y bloqueo ante incertidumbre. `AcceptanceHost --pedidos-smoke` recorrió los servicios REST Desktop de salón y Cocina con API y PostgreSQL temporales, incluyendo transición hasta `Lista` y lectura de la cuenta; cerró su entorno. Usa sesión adaptada: no valida sesión Desktop real, ventana, teclado, render ni tiempos. EF repitió los avisos registrados en ERR-018.

## Revisión realizada por el usuario

El usuario informa que revisó los puntos de conexión/navegación, cola y detalle, transiciones, desaparición de cola y persistencia en la cuenta, además de los restantes controles y estados descritos arriba; confirma que funcionaron. En particular, una comanda marcada lista deja la cola y aparece como lista desde el pedido de mesa. El usuario pidió que el botón Cocina adopte el estilo del elemento Mesas activo en la barra lateral; el cambio acaba de incorporarse y queda pendiente de comprobar visualmente en la compilación actual.

## Alcance que sigue pendiente para D6/operación

- [ ] Volver a capturar/revisar el botón Cocina con su nuevo estilo en ejecución.
- [ ] Prueba física LAN, métricas cronometradas de render, recuperación durable y cualquier requisito de producción permanecen fuera de esta aceptación universitaria.
