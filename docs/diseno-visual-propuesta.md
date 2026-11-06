# Dirección visual — El Carbonazo Desktop

**Estado:** dirección oscura «Carbón & Fuego» y tarjetas rectangulares aprobadas. T9 reorganiza el mapa y añade navegación plegable; la aceptación visual y de teclado se registra por separado.

**T9:** barra lateral plegable en su propia columna, con control permanente y redistribución del mapa; cabecera compacta, conexión textual con indicador verde solo tras consulta satisfactoria, rojo ante fallo de red/respuesta y ámbar para estados no confirmados. La leyenda cuenta los estados de las mesas visibles; sin conexión corresponde a los últimos datos conservados. La acción «Agrupar mesas existentes» y un panel compacto aclaran la selección y confirmación sin crear mesas nuevas. Las tarjetas mantienen texto y bandas luminosas: libre `#27E7A0`, ocupada `#FF5B66`, reservada `#FFBF36` y agrupada `#32D9F4`. El detalle permanece lateral. La versión anterior de T9 recibió una valoración positiva del usuario; estos ajustes posteriores requieren revisión con ratón, teclado, escala y tamaño mínimo.

**Extensión T8 para login y ventana:** se usa la fotografía local `Assets/logo_cabonazo.jfif` como fondo, atenuada para priorizar el formulario. La tarjeta incorpora el acento cálido luminoso `#FFB488` para el título y conserva superficies oscuras; no se cambian los colores semánticos de las mesas. Se añade un control accesible para mostrar/ocultar la contraseña, inicialmente oculta y reiniciada al terminar cada intento. `SukiWindow` usa una barra compacta oscura, con minimizar, maximizar/restaurar y cerrar. La apariencia y el comportamiento se revisan en ejecución, también con teclado, escalado y tamaño mínimo.

## 1. Objetivo

Una aplicación de restaurante moderna, cálida y clara: identidad de marca visible, superficies limpias, acciones rápidas y estados comprensibles durante un turno. El diseño debe funcionar sin Internet y priorizar legibilidad sobre efectos decorativos.

## 2. Dirección aprobada: Carbón & Fuego

- Fondo oscuro y navegación lateral gris humo; identidad y acentos en naranja fuego.
- Área de trabajo oscura con tarjetas, bordes y jerarquía visual inspirados en la referencia.
- Acentos inspirados en brasas para énfasis secundarios, sin competir con los colores de estado de las mesas.
- Tipografía Inter, títulos claros, espaciado consistente y esquinas moderadamente redondeadas.
- Sombras y transiciones sutiles; evitar fondos animados permanentes que distraigan o consuman recursos.

### Paleta definitiva

| Uso | Color propuesto | Estado |
|---|---|---|
| Fondo principal | `#1E1E1E` | Aprobado |
| Barra lateral / paneles | `#2D2D30` | Aprobado |
| Bordes | `#3F3F46` | Aprobado |
| Texto principal | `#F3F4F6` | Aprobado |
| Texto secundario | `#9CA3AF` | Aprobado |
| Acento / hover | `#FF5722` / `#E65100` | Aprobado |
| Libre / Ocupada | `#10B981` / `#EF4444` | Aprobado |
| Reservada / Agrupada | `#F59E0B` / `#06B6D4` | Aprobado |

La referencia propone blanco sobre fondos brillantes, pero el contraste calculado frente a blanco es 2.54:1 (verde), 3.76:1 (rojo), 2.15:1 (ámbar), 2.43:1 (cian) y 3.16:1 (naranja), insuficiente para texto normal de 4.5:1. Preferir estados con fondo oscuro y texto/borde semántico; para fondos brillantes seleccionar texto oscuro u otra variante probada. Rojo `#EF4444` sobre `#1E1E1E` da 4.43:1: requiere ajustar superficie/texto o tamaño, no afirmarlo accesible sin comprobación. Texto y/o icono acompañan al color.

Para T8, el título `#FFB488` sobre una superficie de referencia `#27272B` da aproximadamente 8,60:1; texto `#F3F4F6` sobre `#A93817` da 5,84:1 y sobre `#9F3517` 6,37:1. La tarjeta usa transparencia sobre una fotografía: estos cálculos son de colores de referencia, no sustituyen comprobar el contraste compuesto ni los controles en pantalla.

## 3. Distribución de MesasView

```text
┌────────────────┬──────────────────────────────────────────────┐
│ EL CARBONAZO   │ Mesas                       Usuario · Rol    │
│                │ Estado de conexión          [Cerrar sesión]  │
│ ● Mesas        ├──────────────────────────────────────────────┤
│                │ Leyenda de estados          [Agrupar mesas]  │
│                │                                              │
│                │  Mesa 1      Mesa 2      Mesa 3      …       │
│                │  Libre       Ocupada     Reservada            │
│                │                                              │
│                │              Mapa de 15 mesas                 │
└────────────────┴──────────────────────────────────────────────┘
```

- La navegación muestra únicamente destinos implementados. No agregar botones de Pedidos, Caja u otros módulos que aparenten funcionar si aún no existen.
- La barra lateral puede ser compacta o plegable cuando el ancho de ventana lo requiera. El área de mesas conserva desplazamiento vertical sin recortar controles.
- Usar tarjetas rectangulares oscuras adaptables con acento y etiqueta de estado; se sustituyen las mesas circulares y sillas decorativas anteriores.
- Las tarjetas mantienen un borde discreto en reposo; al pasar el cursor se eleva sutilmente solo esa tarjeta y su borde cambia al acento fuego con una transición breve. La mesa seleccionada conserva el borde de acento sin animación continua. El foco de teclado debe seguir siendo visible. Selección y estado de mesa son indicadores distintos.
- Cada mesa muestra número, capacidad y estado; el detalle de grupo se deriva de los datos disponibles y no inventa pedidos ni importes.
- Al seleccionar una mesa, usar un panel o diálogo claro con las acciones disponibles según estado y rol. Liberar y cerrar grupo requieren confirmación.
- El modo Agrupar muestra selección, contador, Confirmar y Cancelar; no permite elegir mesas no elegibles.

## 4. Estados de interfaz

- **Cargando:** indicador discreto y mensaje, sin presentar mesas ficticias.
- **Conectado:** mapa actualizado y acciones permitidas por rol.
- **Sin conexión:** banner visible, últimos datos en solo lectura y sin cola de cambios.
- **Sesión pausada:** mensaje distinto de desconexión; no renovar automáticamente por polling.
- **Resultado incierto:** advertencia que explica que el cambio podría haberse aplicado, lectura actual y reconocimiento humano. El prototipo no garantiza que una lectura demuestre que la escritura anterior terminó.
- **Conflicto o cuenta pendiente:** mensaje claro sobre qué ocurrió y qué puede hacer el usuario; no reintentar una mutación automáticamente.
- **Seguridad/configuración:** mantener las pantallas bloqueantes de T4, sin bypass TLS.

## 5. Controles y arquitectura

- Mantener Avalonia 12 y SukiUI 7.0.1 existentes, sin agregar paquetes o actualizar versiones por razones visuales.
- Usar `SukiWindow` y `SukiTheme` ya configurados. Evaluar `SukiSideMenu`, diálogos y toasts de SukiUI para navegación y retroalimentación, verificando su API y comportamiento real en la versión instalada antes de adoptarlos.
- Mantener lógica y comandos en ViewModels/servicios; code-behind limitado a inicialización. Los bindings deben seguir compilados.
- Aplicar recursos y estilos reutilizables en lugar de repetir colores y medidas dentro de cada vista. Evitar iconos o imágenes descargadas durante la ejecución.
- No introducir animaciones complejas o shaders como requisito; priorizar fluidez en un equipo de escritorio modesto.

## 6. Accesibilidad y presentación

- Comprobar navegación por teclado, foco visible, nombres accesibles y tamaño de texto; no depender solo del color.
- Revisar contraste de texto normal y estados, escala del sistema y tamaño mínimo de ventana `800×560`.
- Verificar que la barra lateral no oculte el mapa ni los diálogos a ese tamaño y con escalado de Windows.
- Mostrar operaciones destructivas con confirmación clara y mantener visibles los errores de sesión/red.

## 7. Alcance y orden de implementación

1. Aprobar dirección visual, paleta y distribución; actualizar las especificaciones si se cambia el Design System.
2. Terminar coordinación funcional de polling, mutaciones y estados de sesión.
3. Implementar mapa interactivo, acciones por rol, confirmaciones y agrupación.
4. Aplicar el diseño de navegación y componentes reutilizables sin adelantar módulos futuros.
5. Ejecutar build, pruebas focalizadas y revisión visual manual; registrar limitaciones y mejoras posteriores.

## 8. Estado y pendientes

### Propuesta futura 003 — Pedidos y cocina

La documentación en `specs/003-pedidos-kds/` plantea un borrador de comanda con categorías y controles táctiles, resumen de cuenta con envíos separados y una cola de cocina con cantidades y notas destacadas. Es una dirección pendiente de implementación y revisión visual: conservar texto además del color, foco, tamaños accesibles y navegación solo a destinos reales. No afirmar que el KDS o el catálogo ya existen ni prometer notificaciones instantáneas.

- La paleta y las tarjetas están aplicadas al mapa; el usuario verificó el hover y los colores de botones. T7 unifica el login y agrega leyenda y ayudas; no se consideran visualmente aceptados solo por compilar.
- El detalle se presenta en panel lateral; liberar usa una confirmación central. T7 aplica una confirmación central también al cierre de grupo.
- La barra lateral T9 ocupa una columna propia y redistribuye el mapa; revisar el tamaño mínimo `800×560`, escalado, navegación y foco.
- Hay cálculos de contraste y verificaciones parciales en `docs/validacion-t5.md` y `docs/validacion-t6.md`; siguen pendientes la revisión visual exhaustiva y los controles interactivos en ejecución.
