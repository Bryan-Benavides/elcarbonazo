# Dirección visual — El Carbonazo Desktop

**Estado: paleta oscura «Carbón & Fuego» y tarjetas rectangulares aprobadas.** No representa funcionalidades ya implementadas; el panel de detalle y la barra plegable se definirán al integrar las acciones.

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

## 8. Pendientes de decisión

- Paleta «Carbón & Fuego» aprobada; falta aplicarla a la interfaz y probar contraste visual.
- ¿Detalle de mesa en panel lateral o en diálogo? El panel permite comparar el mapa; el diálogo es más sencillo en pantallas pequeñas.
- ¿Barra lateral expandida siempre o plegable? Confirmar según el espacio disponible a `800×560`.
- La propuesta aún no ha sido probada visualmente ni revisada mediante mediciones de contraste.
