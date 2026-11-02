---
name: sukiui-docs
description: Documentación de referencia de SukiUI 7.0.1 para Avalonia UI (compatible con Avalonia 12). Úsala cuando necesites configurar SukiTheme, SukiWindow, fondos con shaders SKSL, diálogos, toasts, controles personalizados (GlassCard, Stepper, WaveProgress, InfoBar, SukiSideMenu) o tematización en aplicaciones de escritorio Avalonia 12. NOTA: no hay changelog oficial de 7.0.1; la API documentada proviene de la documentación oficial del repositorio y los releases de 7.x.
---

## Bloque 1: Versión 7.0.1 — Estado, compatibilidad y paquetes opcionales

**SukiUI 7.0.1** es la release diseñada para dar soporte oficial a **Avalonia 12**.

**Compatibilidad crítica**:
- **SukiUI 6.0 no es compatible con Avalonia 12**. Si tu proyecto está construido sobre Avalonia 12, debes utilizar **SukiUI 7.x** (7.0.1 o superior).
- **Alcance**: SukiUI está enfocado **exclusivamente en aplicaciones de escritorio** (Windows, macOS, Linux). Para proyectos móviles con estética Suki, la documentación oficial recomienda la librería **CherylUI**.

**Instalación base vía NuGet**:

```bash
dotnet add package SukiUI --version 7.0.1
```

**Paquetes opcionales**:
El paquete núcleo (`SukiUI`) no incluye dependencias adicionales ni temas de controles específicos. Si los requieres en tu proyecto, instala los paquetes correspondientes:

| Paquete | Propósito | Comando de instalación |
|---|---|---|
| `SukiUI.ConditionalXAML` | Helpers para renderizado condicional en XAML (`If`, `InlineSharp`) | `dotnet add package SukiUI.ConditionalXAML` |
| `SukiUI.ColorPicker` | Tema personalizado para el control `ColorPicker` de Avalonia | `dotnet add package SukiUI.ColorPicker` |
| `SukiUI.DataGrid` | Tema personalizado para el control `DataGrid` de Avalonia | `dotnet add package SukiUI.DataGrid` |
| `SukiUI.Dock` | Integración de diseño y desacoplamiento de paneles de ventana | `dotnet add package SukiUI.Dock` |

---

## Bloque 2: Configuración inicial en App.axaml

Para inicializar la tematización de SukiUI, debes registrar `SukiTheme` dentro de `<Application.Styles>` en tu archivo `App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:sukiUi="clr-namespace:SukiUI;assembly=SukiUI"
             x:Class="MyApp.App">
    <Application.Styles>
        <sukiUi:SukiTheme ThemeColor="Blue" />
    </Application.Styles>
</Application>
```

> **Advertencia importante**: Si no declaras `<sukiUi:SukiTheme />` o no defines un `ThemeColor`, la ventana y la mayoría de los controles se renderizarán completamente transparentes.

**Localización e idioma integrados**:
Por defecto, los componentes contextuales de Avalonia usan inglés. Para cambiar la localización de los diálogos y menús del sistema:

```xml
<sukiUi:SukiTheme ThemeColor="Blue" Locale="es-ES" />
```

**Registro de paquetes opcionales en `App.axaml`**:

Si utilizas `ColorPicker` o `DataGrid`, agrega sus temas **después** de `SukiTheme`:

```xml
<!-- ColorPicker -->
<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"/>
<sukiUi:SukiTheme ThemeColor="Blue" />
<StyleInclude Source="avares://SukiUI.ColorPicker/Theme/Index.axaml"/>

<!-- DataGrid -->
<StyleInclude Source="avares://SukiUI.DataGrid/Theme/Index.axaml"/>
```

---

## Bloque 3: SukiWindow como ventana principal

`SukiWindow` reemplaza a la clase `Window` nativa de Avalonia para proveer la barra de título personalizada, los hosts de diálogos/toasts y los fondos dinámicos.

**Modificación en XAML (`MainWindow.axaml`)**:

```xml
<sukiUi:SukiWindow x:Class="MyApp.MainWindow"
                   xmlns="https://github.com/avaloniaui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   xmlns:sukiUi="clr-namespace:SukiUI.Controls;assembly=SukiUI"
                   Title="Mi Aplicación Avalonia 12"
                   BackgroundStyle="Bubble"
                   IsMenuVisible="True">
    
    <sukiUi:SukiWindow.LogoContent>
        <!-- Icono o logo en la barra de título -->
        <PathIcon Data="{StaticResource AppLogoGeometry}" Width="20" Height="20"/>
    </sukiUi:SukiWindow.LogoContent>

    <sukiUi:SukiWindow.RightWindowTitleBarControls>
        <!-- Controles adicionales a la derecha de la barra de título -->
        <Button Classes="Basic" Content="Ayuda"/>
    </sukiUi:SukiWindow.RightWindowTitleBarControls>

    <!-- Contenido principal -->
    <Grid>
        <TextBlock Text="¡Bienvenido a SukiUI 7.0.1!" HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</sukiUi:SukiWindow>
```

**Modificación en Code-Behind (`MainWindow.axaml.cs`)**:

```csharp
using SukiUI.Controls;

namespace MyApp;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

**Propiedades destacadas de `SukiWindow`**:
- `BackgroundStyle`: Define el estilo de fondo (`Bubble`, `Gradient`, `Flat`).
- `LogoContent`: Permite incrustar un logo personalizado en el extremo izquierdo de la barra de título.
- `RightWindowTitleBarControls`: Permite insertar elementos XAML personalizados antes de los botones de minimizar/cerrar.
- `IsMenuVisible`: Alterna la visibilidad de la barra de menú integrada.
- `CanMinimize` / `CanMaximize`: Controla la disponibilidad de botones de ventana.
- `TitleBarAnimationEnabled`: Activa animaciones al interactuar con la barra superior.

---

## Bloque 4: Fondos dinámicos (SukiBackground y Shaders SKSL)

SukiUI integra un motor de renderizado de fondos acelerado por GPU que ejecuta shaders **SKSL** (Skia Shading Language) actualizados dinámicamente según el tema seleccionado.

**Propiedades de configuración en `SukiWindow`**:

| Propiedad | Tipo | Descripción |
|---|---|---|
| `BackgroundStyle` | `SukiBackgroundStyle` | `Gradient`, `Flat` o `Bubble`. |
| `BackgroundShaderFile` | `string` | Nombre del recurso embebido `.sksl` (ej. `Cells`, `Waves`). |
| `BackgroundShaderCode` | `string` | Código de shader SKSL directo como string. |
| `BackgroundTransitionsEnabled` | `bool` | Habilita transición con desvanecido (fade) al cambiar de estilo. |
| `BackgroundTransitionTime` | `double` | Duración de la transición en segundos (por defecto 1.0 s). |
| `BackgroundAnimationEnabled` | `bool` | Habilita la animación continua a los FPS nativos de la GPU. |

> **Prioridad de evaluación**: Si coinciden varias propiedades, SukiUI evalúa en este orden: `BackgroundShaderFile` $\rightarrow$ `BackgroundShaderCode` $\rightarrow$ `BackgroundStyle`.

**Uniforms inyectados automáticamente en shaders SKSL**:

| Uniform | Tipo GLSL/SKSL | Descripción |
|---|---|---|
| `iTime` | `float` | Tiempo transcurrido en segundos (avanza si la animación está activa). |
| `iDark` | `float` | `1.0` si el tema es Dark, `0.0` si es Light. |
| `iAlpha` | `float` | Opacidad general del fondo. |
| `iResolution` | `vec3` | Resolución en píxeles (`x`, `y`). |
| `iPrimary` | `vec3` | Color primario del tema activo. |
| `iAccent` | `vec3` | Color de acento activo. |
| `iBase` | `vec3` | Color base de fondo del tema. |

**Ejemplo de Shader SKSL personalizado (Flat con color base)**:

```glsl
vec4 main(vec2 fragCoord) {
    return vec4(iBase, iAlpha);
}
```

**Nota de rendimiento y fallback**:
- La animación de fondo activa invalida el cuadro entero de la ventana, aumentando el uso de GPU/CPU en ~5-20%. Se recomienda desactivar en dispositivos de bajo consumo.
- Si SkiaSharp no detecta aceleración por hardware (OpenGL/DirectX), SukiUI utilizará automáticamente un renderizado por software basado en el estilo `Flat`.

---

## Bloque 5: Tematización y SukiTheme API

La clase `SukiTheme` gestiona el estado global de colores y temas claro/oscuro en tiempo de ejecución.

**Obtener la instancia Singleton**:

```csharp
using SukiUI;

SukiTheme theme = SukiTheme.GetInstance();
```

**Operaciones comunes de tema**:

```csharp
// Cambiar a modo Oscuro
SukiTheme.GetInstance().ChangeBaseTheme(ThemeVariant.Dark);

// Cambiar a modo Claro
SukiTheme.GetInstance().ChangeBaseTheme(ThemeVariant.Light);

// Alternar automáticamente entre Claro y Oscuro
SukiTheme.GetInstance().SwitchBaseTheme();

// Cambiar la paleta de colores de acento
SukiTheme.GetInstance().ChangeColorTheme(SukiColor.Green);
```

**Suscripción a eventos de cambio de tema**:

```csharp
SukiTheme.GetInstance().OnBaseThemeChanged += (ThemeVariant newVariant) =>
{
    Console.WriteLine($"El tema cambió a: {newVariant}");
};

SukiTheme.GetInstance().OnColorThemeChanged += (SukiColor newColor) =>
{
    Console.WriteLine($"El color primario cambió a: {newColor}");
};
```

---

## Bloque 6: Hosts (Diálogos y Toasts)

`SukiWindow` incluye hosts integrados que permiten renderizar elementos por encima de todos los controles visuales.

### 1. Diálogos Modales (`ISukiDialogManager`)

**Configuración en ViewModel**:

```csharp
using SukiUI.Dialogs;

public partial class MainViewModel : ObservableObject
{
    public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();

    public void MostrarDialogoConfirmacion()
    {
        DialogManager.CreateDialog()
            .WithTitle("Confirmar Operación")
            .WithContent("¿Está seguro de que desea guardar los cambios en la base de datos?")
            .WithActionButton("Guardar", _ => EjecutarGuardado(), dismissOnClick: true)
            .WithActionButton("Cancelar", _ => { }, dismissOnClick: true)
            .Dismiss().ByClickingBackground()
            .TryShow();
    }

    private void EjecutarGuardado() { /* ... */ }
}
```

### 2. Notificaciones Flotantes (`ISukiToastManager`)

**Configuración y encolado de Notificaciones**:

```csharp
using SukiUI.Toasts;

public partial class MainViewModel : ObservableObject
{
    public ISukiToastManager ToastManager { get; } = new SukiToastManager();

    public void NotificarExito()
    {
        ToastManager.CreateToast()
            .WithTitle("Operación Exitosa")
            .WithContent("Los datos se han sincronizado correctamente.")
            .AsSuccess()
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Queue();
    }
}
```

**Tipos de Toast disponibles**: `AsInfo()`, `AsSuccess()`, `AsWarning()`, `AsError()`, `AsLoading()`.

---

## Bloque 7: SukiSideMenu (Navegación lateral)

`SukiSideMenu` ofrece un panel de navegación estructurado con soporte para búsqueda, secciones superiores/inferiores y menú anidado.

```xml
<suki:SukiSideMenu IsSearchEnabled="True"
                   IsToggleButtonVisible="True"
                   OpenPaneLength="250">
    
    <!-- Encabezado del menú -->
    <suki:SukiSideMenu.HeaderContent>
        <StackPanel Margin="15">
            <TextBlock Text="El Carbonazo POS" FontSize="16" FontWeight="Bold"/>
            <TextBlock Text="v1.0.0" Foreground="{DynamicResource SukiLowText}"/>
        </StackPanel>
    </suki:SukiSideMenu.HeaderContent>

    <!-- Items de menú -->
    <suki:SukiSideMenuItem Header="Ventas y Caja">
        <suki:SukiSideMenuItem.PageContent>
            <views:PosMainView />
        </suki:SukiSideMenuItem.PageContent>
    </suki:SukiSideMenuItem>

    <!-- Menú desplegable con SubItems -->
    <suki:SukiSideMenuItem Header="Inventario">
        <suki:SukiSideMenuItem.Items>
            <suki:SukiSideMenuItem Header="Productos y Recetas">
                <suki:SukiSideMenuItem.PageContent>
                    <views:ProductsView />
                </suki:SukiSideMenuItem.PageContent>
            </suki:SukiSideMenuItem>
            <suki:SukiSideMenuItem Header="Stock y Proveedores">
                <suki:SukiSideMenuItem.PageContent>
                    <views:StockView />
                </suki:SukiSideMenuItem.PageContent>
            </suki:SukiSideMenuItem>
        </suki:SukiSideMenuItem.Items>
    </suki:SukiSideMenuItem>

    <!-- Pie del menú -->
    <suki:SukiSideMenu.FooterContent>
        <Button Classes="Basic" Content="Cerrar Sesión" HorizontalAlignment="Stretch" Margin="10"/>
    </suki:SukiSideMenu.FooterContent>
</suki:SukiSideMenu>
```

> **Regla de integridad**: Cada `SukiSideMenuItem` asignado como hoja de navegación **debe contener un `PageContent` válido**. Si el contenido está vacío, Avalonia puede emitir una excepción de árbol visual (`InvalidOperationException`).

---

## Bloque 8: Catálogo de Controles Especializados de SukiUI

SukiUI provee controles de interfaz optimizados con diseño translucido y retroalimentación visual avanzada:

### Contenedores y Estructura
- **`GlassCard`**: Tarjeta contenedora con efecto de cristal translúcido (glassmorphism), esquinas redondeadas y elevación adaptable al tema activo. Ideal para agrupar formularios o métricas.
- **`SettingsLayout`**: Plantilla de diseño para listas de configuración estilo sistema operativo con títulos, descripciones y switches de control.

### Estado y Retroalimentación
- **`InfoBar`**: Barra de alerta contextual para mostrar mensajes informativos, advertencias o errores con botones de acción embebidos.
- **`InfoBadge`**: Indicador numérico o de estado en forma de badge sobre botones o iconos de menú.
- **`BusyArea`**: Control de bloqueo que muestra una capa translúcida con spinner de carga sobre áreas de la interfaz ocupadas en procesos asíncronos.

### Indicadores de Progreso
- **`WaveProgress`**: Indicador circular de porcentaje animado con efecto fluido de onda de agua.
- **`Stepper`**: Control secuencial para guiar al usuario paso a paso en flujos o asistentes (wizards).
- **`CircleProgressBar` / `Loading`**: spinners e indicadores de carga estilizados.

---

## Bloque 9: Resumen de limitaciones documentales para 7.0.1

Al trabajar con **SukiUI 7.0.1**, ten en cuenta las siguientes consideraciones arquitectónicas:

1. **Documentación base**: La documentación en la rama `main` del repositorio original referencia la arquitectura 6.0 como la última versión con rediseño completo de API, pero la release **7.0.1** es la rama oficial de migración para **Avalonia 12**.
2. **Estabilidad de APIs**: Las APIs clave (`SukiTheme`, `SukiWindow`, `SukiBackground`, `SukiSideMenu`, `ISukiDialogManager`, `ISukiToastManager`) se mantienen idénticas en firma y comportamiento respecto a la documentación de referencia.
3. **Manejo de errores de versión**: Si al compilar en Avalonia 12 obtienes un error tipo `MissingMethodException` o inconformidad de tipos en la barra de título, verifica que todos los paquetes relacionados (`SukiUI`, `SukiUI.DataGrid`, `SukiUI.ColorPicker`) compartan estrictamente la versión `7.0.1` o builds nightly `7.0.2` compatibles.