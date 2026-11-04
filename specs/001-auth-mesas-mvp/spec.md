# Especificación del Módulo 001: Autenticación, Sesiones y Control de Mesas (MVP)

## Contexto y Alcance
Este módulo establece la seguridad de acceso, la sesión operativa y el control concurrente de las 15 mesas del restaurante. Incluye la base de datos necesaria para validar cuentas pendientes y grupos de mesas, aunque la captura funcional de pedidos se implementará en un módulo posterior.

La solución opera únicamente en la LAN del negocio, sin dependencia de Internet. Desktop consume la API por HTTP/REST y nunca accede directamente a PostgreSQL.

Quedan fuera de este módulo las altas de empleados, cambios de rol, desactivación y restablecimiento de contraseñas. Una especificación posterior debe implementar esas funciones antes de la operación completa con los ocho colaboradores; no se sustituyen con SQL manual ni comandos permanentes.

## Requisitos Funcionales (EARS)

### RF-01: Autenticación e Identidad
- **CUANDO** un empleado ingrese nombre y contraseña válidos, **EL SISTEMA DEBE** autenticarlo mediante un hash BCrypt con sal y crear una única sesión activa para ese usuario.
- **CUANDO** se busque o almacene un nombre o email, **EL SISTEMA DEBE** aplicar Unicode NFC, Trim y minúsculas en ese orden, y **DEBE** impedir duplicados de ambos campos.
- **CUANDO** se cree o actualice un nombre de login, **EL SISTEMA DEBE** exigir 3–50 caracteres, extremos alfanuméricos Unicode y únicamente espacio, punto, guion o guion bajo como separadores internos simples.
- **CUANDO** se cree o actualice un email, **EL SISTEMA DEBE** validar localmente su sintaxis y un máximo de 254 caracteres, sin consultar DNS ni servicios externos.
- **CUANDO** una contraseña nueva o de bootstrap se valide fuera de Development, **EL SISTEMA DEBE** exigir al menos 10 caracteres, no más de 64 bytes UTF-8 y rechazo de una lista local mínima de contraseñas comunes, sin imponer categorías de composición.
- **CUANDO** ocurran cinco intentos fallidos consecutivos para un usuario, **EL SISTEMA DEBE** bloquear nuevos logins durante cinco minutos.
- **CUANDO** venza ese bloqueo, **EL SISTEMA DEBE** reiniciar el contador antes del siguiente intento y otorgar cinco intentos nuevos.
- **MIENTRAS** una IP supere 10 solicitudes de login en un minuto, **EL SISTEMA DEBE** rechazar los excedentes con 429 y `Retry-After` sin ejecutar BCrypt.
- **SI** las credenciales son incorrectas, la cuenta está inactiva o permanece bloqueada, **EL SISTEMA DEBE** responder de forma genérica sin revelar cuál condición ocurrió.
- **SI** no corresponde verificar el hash real por usuario inexistente, inactivo o bloqueado, **EL SISTEMA DEBE** realizar una verificación BCrypt señuelo con el costo configurado para reducir enumeración por tiempo.
- **CUANDO** el costo BCrypt configurado sea superior al del hash existente y el login sea válido, **EL SISTEMA DEBE** actualizar el hash antes de completar la autenticación.

### RF-02: Ciclo de Sesión
- **CUANDO** el login sea válido, **EL SISTEMA DEBE** emitir un access token JWT de cinco minutos y un refresh token rotatorio, y devolver por separado los vencimientos UTC de inactividad y absoluto.
- **CUANDO** login, refresh o activity respondan correctamente, **EL SISTEMA DEBE** incluir `serverTime` UTC para que Desktop compense el desfase de su reloj.
- **MIENTRAS** exista una sesión, **EL SISTEMA DEBE** cerrarla después de 15 minutos sin interacción humana o al alcanzar ocho horas desde su creación, lo que ocurra primero.
- **CUANDO** falte un minuto para el vencimiento por inactividad, **EL SISTEMA DEBE** mostrar cuenta regresiva con Continuar/Cerrar sesión; solo Continuar debe registrar actividad y la ausencia de respuesta debe cerrar al vencer.
- **CUANDO** falten cinco minutos para el límite absoluto, **EL SISTEMA DEBE** mostrar una cuenta regresiva no extensible y exigir login al llegar a cero.
- **CUANDO** Desktop detecte interacción humana, **EL SISTEMA DEBE** permitir reportarla como máximo una vez por minuto sin rotar tokens; polling y tareas automáticas **NO DEBEN** mantener abierta la sesión.
- **SI** llegan reportes adicionales dentro del mismo minuto, **EL SISTEMA DEBE** responder con las expiraciones vigentes sin realizar otra escritura.
- **CUANDO** se renueve una sesión válida, **EL SISTEMA DEBE** rotar el refresh token y conservar únicamente hashes SHA-256 de tokens emitidos/consumidos.
- **CUANDO** se emita un refresh token, **EL SISTEMA DEBE** generarlo con 32 bytes criptográficamente aleatorios y codificarlo como Base64URL sin padding.
- **SI** se reutiliza un refresh token consumido, **EL SISTEMA DEBE** revocar inmediatamente la sesión completa y exigir un nuevo login.
- **CUANDO** ocurra logout, nuevo login del mismo usuario o cierre normal de Desktop, **EL SISTEMA DEBE** revocar la sesión inmediatamente cuando la API esté disponible.
- **SI** Desktop termina inesperadamente o pierde conexión, **EL SISTEMA DEBE** dejar que la sesión venza por inactividad.
- **CUANDO** el usuario confirme `Cerrar sesión` en el encabezado, **EL SISTEMA DEBE** detener tareas, intentar logout, limpiar tokens y volver a LoginView aunque la API no responda.
- **SI** el access token vence mientras solo se ejecuta polling, **EL SISTEMA DEBE** pausar consultas y mantener la vista solo para lectura hasta que una interacción humana inicie refresh; no debe renovar automáticamente.
- **CUANDO** exista interacción humana y al access token le queden 60 segundos o menos, **EL SISTEMA DEBE** completar un único refresh compartido antes de continuar con la acción.
- **SI** una acción humana recibe `AUTH_ACCESS_TOKEN_EXPIRADO`, **EL SISTEMA DEBE** renovar y reintentar la acción una sola vez; no debe reintentar automáticamente otros errores o timeouts.

### RF-03: Consulta de Mesas
- **MIENTRAS** el usuario tenga cualquiera de los cinco roles autorizados, **EL SISTEMA DEBE** mostrar las 15 mesas con número, capacidad, grupo, estado y versión.
- **CUANDO** el estado de una mesa cambie y API/red estén saludables, **EL SISTEMA DEBE** iniciar polling con cadencia nominal de dos segundos, sin solapar solicitudes. La latencia observable de convergencia se mide como espera hasta el siguiente GET más su duración y el despacho/render; el objetivo nominal es 2 s más la latencia medida de GET y presentación, no un límite absoluto de 2 s. Nunca se solapan solicitudes para aparentar cumplirlo.
- **MIENTRAS** Desktop realice polling, **EL SISTEMA NO DEBE** considerarlo actividad humana ni generar un evento de auditoría por cada consulta.
- **SI** una consulta sigue en curso o la API falla, **EL SISTEMA NO DEBE** solapar polling; debe aplicar backoff 2→5→10→30 segundos, mostrar estado sin conexión y recuperar automáticamente el intervalo normal tras un éxito.
- **MIENTRAS** Desktop indique estado sin conexión, **EL SISTEMA DEBE** conservar la última vista solo para lectura, bloquear mutaciones y no encolar cambios offline.
- **SI** una lectura supera 5 segundos o una escritura/login supera 10 segundos, **EL SISTEMA DEBE** cancelar la espera; una escritura con resultado desconocido no debe reintentarse automáticamente y debe reconciliarse mediante GET.

### RF-04: Cambio de Estado de Mesa
- **CUANDO** se pulse una mesa individual fuera del modo de agrupación, **EL SISTEMA DEBE** mostrar su detalle y únicamente las acciones válidas por estado y rol; Liberar debe exigir confirmación.
- **CUANDO** una Jefa, Encargado de Caja, Mesero o Supervisor solicite un cambio válido, **EL SISTEMA DEBE** aplicar la transición de forma atómica y devolver la mesa completa con su nueva versión.
- **MIENTRAS** el usuario tenga rol Personal de Cocina, **EL SISTEMA DEBE** permitir consulta de mesas, pero **NO DEBE** permitir cambios de estado ni agrupaciones.
- **CUANDO** se solicite una transición, **EL SISTEMA DEBE** permitir únicamente Libre→Reservada/Ocupada, Reservada→Libre/Ocupada y Ocupada→Libre sin pedidos pendientes.
- **SI** una mesa pertenece a un grupo activo, **EL SISTEMA NO DEBE** permitir cambios individuales ni asignar el estado Agrupada desde el endpoint de estado.
- **SI** la versión enviada quedó obsoleta, **EL SISTEMA DEBE** rechazar el cambio con conflicto sin sobrescribir la actualización más reciente.
- **CUANDO** Desktop reciba un conflicto de concurrencia, **EL SISTEMA DEBE** recargar el estado, informar el cambio ajeno y exigir nueva confirmación sin reintento automático.
- **SI** la versión está vigente y el estado solicitado ya coincide, **EL SISTEMA DEBE** responder 200 con la mesa actual sin escribir, incrementar versión ni auditar; una versión obsoleta conserva prioridad y responde conflicto.

### RF-05: Agrupación de Mesas
- **MIENTRAS** el rol permita modificar mesas, **EL SISTEMA DEBE** ofrecer en Desktop selección de 2–15 mesas elegibles, confirmación de creación y cierre del grupo; Personal de Cocina solo debe visualizarlo.
- **CUANDO** se active `Agrupar mesas`, **EL SISTEMA DEBE** entrar en un modo explícito que solo seleccione mesas Libres, muestre el contador y ofrezca Confirmar/Cancelar.
- **CUANDO** se pulse una mesa Agrupada, **EL SISTEMA DEBE** mostrar el ID y las mesas integrantes; un rol autorizado solo puede cerrar después de una confirmación explícita.
- **MIENTRAS** exista una selección para agrupar, **EL SISTEMA DEBE** mantener polling, usar las versiones más recientes y retirar con aviso cualquier mesa que deje de ser elegible.
- **CUANDO** un rol con permiso seleccione entre 2 y 15 mesas distintas, **EL SISTEMA DEBE** agruparlas solo si todas están Libres, sin grupo, sin pedidos pendientes y conservan las versiones leídas.
- **CUANDO** se cree un grupo, **EL SISTEMA DEBE** asignar el mismo grupo y el estado Agrupada a todas sus mesas en una única transacción.
- **CUANDO** se cree un grupo, **EL SISTEMA DEBE** conservar una relación histórica inmutable por cada mesa; cerrar el grupo no debe borrar sus miembros históricos.
- **CUANDO** se cree o cierre un grupo, **EL SISTEMA DEBE** conservar respectivamente el usuario creador y el usuario de cierre mediante relaciones históricas restrictivas.
- **CUANDO** la creación termine, **EL SISTEMA DEBE** devolver el grupo y las mesas completas con sus versiones nuevas.
- **SI** cualquier mesa dejó de ser elegible o cambió de versión, **EL SISTEMA DEBE** cancelar la creación completa sin cambios parciales.
- **CUANDO** se cierre un grupo activo sin pedido pendiente, **EL SISTEMA DEBE** conservar su historial, dejar sus mesas Libres y retirar su relación con el grupo en una única transacción.
- **SI** el grupo tiene un pedido Abierto, En Preparación o Listo, **EL SISTEMA NO DEBE** permitir su cierre.
- **MIENTRAS** el cierre compita con otra operación, **EL SISTEMA DEBE** revalidar el grupo, sus mesas y pedidos bajo control transaccional antes de confirmar.
- **SI** el grupo existe pero ya está cerrado, **EL SISTEMA DEBE** responder 204 sin escribir ni auditar nuevamente; un ID inexistente debe responder 404.
- **CUANDO** Desktop reciba 204 al cerrar, **EL SISTEMA DEBE** consultar mesas inmediatamente y mantener acciones deshabilitadas hasta reconciliar versiones.

### RF-06: Reglas Mínimas de Pedido
- **CUANDO** exista un pedido, **EL SISTEMA DEBE** asociarlo exclusivamente a una mesa o a un grupo, nunca a ambos ni a ninguno.
- **MIENTRAS** una mesa o grupo tenga un pedido Abierto, En Preparación o Listo, **EL SISTEMA NO DEBE** permitir otro pedido activo para el mismo destino.
- **CUANDO** un grupo tenga pedido, **EL SISTEMA DEBE** tratarlo como una cuenta compartida asociada al grupo.
- **CUANDO** se persistan importes de pedido, **EL SISTEMA DEBE** usar NIO con precisión de dos decimales, valores no negativos y total igual a subtotal más impuestos.
- **CUANDO** un pedido individual se abra en un módulo posterior, **EL SISTEMA DEBE** ocupar la mesa en la misma transacción; pagarlo o cancelarlo solo habilitará una liberación explícita.

### RF-07: Auditoría Operativa
- **CUANDO** ocurra un resultado de autenticación, bloqueo, refresh, logout, revocación, bootstrap, cambio de mesa o creación/cierre de grupo, **EL SISTEMA DEBE** insertar un evento de auditoría persistente.
- **MIENTRAS** una IP permanezca limitada, **EL SISTEMA DEBE** auditar solo el primer 429 de cada ventana de un minuto para impedir crecimiento provocado de la tabla.
- **CUANDO** una operación auditable modifique datos, **EL SISTEMA DEBE** guardar el cambio y su auditoría en la misma transacción; si la auditoría falla, debe revertir la operación.
- **MIENTRAS** la aplicación opere, **EL SISTEMA NO DEBE** editar ni eliminar eventos existentes; las correcciones se representan con eventos adicionales.
- **CUANDO** un login falle sin usuario identificado, **EL SISTEMA DEBE** guardar únicamente el nombre intentado normalizado, nunca la contraseña ni el request completo.
- **CUANDO** un evento provenga de Desktop, **EL SISTEMA DEBE** registrar la IP observada y el UUID de instalación como metadatos, sin usarlos para autenticar o autorizar.
- **MIENTRAS** se ejecute el módulo 001, **EL SISTEMA NO DEBE** exponer endpoints ni UI de consulta de auditoría.

### RF-08: Bootstrap de Producción
- **CUANDO** una instalación de producción no tenga usuarios o contenga únicamente la fila heredada neutralizada, **EL SISTEMA DEBE** permitir crear la primera Jefa mediante un comando explícito y entrada de credenciales no versionada.
- **SI** existe únicamente la fila heredada neutralizada, **EL SISTEMA DEBE** reutilizar esa fila y conservar su ID.
- **SI** existe cualquier otro usuario, **EL SISTEMA NO DEBE** permitir ejecutar el bootstrap.
- **MIENTRAS** el entorno no sea Development, **EL SISTEMA NO DEBE** crear credenciales predeterminadas.
- **SI** la fila histórica de admin todavía coincide exactamente con nombre, email y hash del seed conocido, **EL SISTEMA DEBE** desactivarla y neutralizar su hash sin eliminarla ni afectar cuentas modificadas.
- **MIENTRAS** el entorno sea Development y no exista usuario o solo exista la fila heredada neutralizada, **EL SISTEMA PUEDE** crear o reconfigurar `admin/admin123` como excepción local reproducible.

## Requisitos No Funcionales

### RNF-01: Contratos HTTP
- Requests y responses **DEBEN** usar JSON camelCase; el número físico se expone únicamente como `numero`.
- Errores 4xx/5xx **DEBEN** usar `application/problem+json` con `status`, `code` y `traceId` estables.
- Requests con campos inválidos **DEBEN** devolver 400 `ValidationProblemDetails`, código `VALIDATION_ERROR` y `errors` por campo en camelCase.
- 401 **DEBE** representar falta/expiración de autenticación o sesión; 403 **DEBE** representar un rol autenticado sin permiso. Desktop **NO DEBE** intentar refresh ante 403.

### RNF-02: Seguridad y Operación LAN
- Producción **DEBE** publicar la API mediante HTTPS y un certificado confiable para `elcarbonazo-api.local`; Desktop **NO DEBE** desactivar validación TLS.
- **SI** falla la validación TLS, Desktop **DEBE** detener tareas, limpiar la sesión local y mostrar un bloqueo de seguridad no evadible, distinto del modo sin conexión.
- URL, certificado, conexión PostgreSQL, JWT y demás secretos **DEBEN** configurarse fuera del código y de archivos versionados.
- En Windows, `ApiBaseUrl` e `idInstalacion` **DEBEN** almacenarse por equipo en `%ProgramData%\ElCarbonazo\desktop.json`, creado por el instalador y de solo lectura para usuarios normales.
- **SI** esa configuración falta o es inválida en producción, Desktop **DEBE** bloquear LoginView y mostrar una pantalla no editable con la ruta esperada; **NO DEBE** usar localhost ni solicitar otra URL.
- Migraciones de producción **DEBEN** ejecutarse explícitamente con un rol administrativo; la API runtime **NO DEBE** tener permisos DDL.
- Producción **DEBE** generar diariamente un respaldo PostgreSQL cifrado en un disco o NAS separado, conservar 30 días y comprobar mensualmente una restauración aislada.
- Access/refresh tokens **DEBEN** permanecer solo en memoria de Desktop.
- Access tokens **DEBEN** firmarse con HS256 y una clave aleatoria externa de al menos 32 bytes, validada al arrancar junto con issuer y audience.
- La validación JWT **DEBE** limitar la tolerancia de reloj a 30 segundos.

### RNF-03: Tiempo y Concurrencia
- Backend y PostgreSQL **DEBEN** persistir, comparar y emitir instantes UTC; Desktop convierte únicamente para mostrar en `America/Managua`.
- Cambios de mesa **DEBEN** usar concurrencia optimista y operaciones multi-fila **DEBEN** ser atómicas y usar un orden de bloqueo consistente cuando corresponda.

### RNF-04: Logs Técnicos
- Producción **DEBE** escribir logs estructurados JSON diarios y consola, con 100 MB máximos por archivo y retención de 30 días.
- La aplicación **DEBE** usar nivel Information y Microsoft/EF/Npgsql nivel Warning; polling y GET exitosos **NO DEBEN** producir un log por petición.
- Logs y auditoría **NO DEBEN** contener contraseñas, tokens completos, claves, cadenas de conexión ni secretos de certificados.
- Desktop **DEBE** escribir logs JSON locales de hasta 20 MB por día con retención de 7 días, registrando transiciones de conectividad y fallos sin generar una entrada por polling exitoso.

### RNF-05: Calidad
- La API **DEBE** mantener Controllers delgados y separar DTOs, servicios, reglas, seguridad y persistencia dentro del proyecto existente.
- Pruebas xUnit unitarias y de integración **DEBEN** ejecutarse contra una base PostgreSQL temporal `elcarbonazo_test_<id>`, nunca contra desarrollo.
- Desktop **DEBE** tener pruebas xUnit unitarias de servicios y ViewModels con HTTP/reloj falsos; la presentación XAML se valida manualmente sin automatización UI completa.
- La lógica crítica de autenticación, sesiones, mesas, grupos y pedidos **DEBE** alcanzar al menos 80% de cobertura.
- La lógica crítica Desktop de servicios y ViewModels **DEBE** alcanzar al menos 80%, excluyendo XAML, code-behind mínimo, código generado y composición.
- Antes de T4 **DEBEN** pasar formato, build, pruebas unitarias, pruebas de integración y auditoría de paquetes vulnerables.

## Criterios de Finalización
- T3.0–T3.11 completadas y verificadas en orden; T4 permanece bloqueada hasta entonces.
- Login, refresh, actividad y logout cumplen los contratos y límites de sesión definidos.
- Los cinco roles consultan mesas; solo Jefa, Encargado de Caja, Mesero y Supervisor pueden modificarlas o agruparlas.
- Las 15 mesas se renderizan con los cuatro estados; polling nominal de dos segundos sin solapamiento y latencia de convergencia medida/documentada como espera de polling más GET y presentación bajo condiciones LAN saludables.
- Migraciones funcionan sobre una base vacía y sobre la base de desarrollo existente sin reescribir `InitialCreate` ni perder datos.
- Una tabla `pedido` preexistente fuera del historial EF solo se adopta después de un preflight compatible; cualquier problema aborta transaccionalmente con diagnóstico y sin cambios parciales.
- Auditoría, logs y configuración LAN operan sin Internet y sin exponer secretos.
- Servicios/ViewModels Desktop superan pruebas automatizadas de sesión, red, polling, conflictos y navegación; el diseño visual supera la lista manual de aceptación.
