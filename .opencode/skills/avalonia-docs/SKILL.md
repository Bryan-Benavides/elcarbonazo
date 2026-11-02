---
name: avalonia-docs
description: Documentación de referencia de Avalonia UI (versión 12). Úsala cuando necesites consultar XAML, bindings compilados, estilos, MVVM, layout o plataformas soportadas en Avalonia.
---

## Bloque 1: Fundamentos y arquitectura de Avalonia

Avalonia es un framework de UI multiplataforma de código abierto para .NET que utiliza su propio motor de renderizado para dibujar controles, por lo que el aspecto y comportamiento de la aplicación es idéntico en todas las plataformas. Escribes la UI una vez en C# o F# con XAML y despliegas a Windows 10/11, macOS (Apple Silicon e Intel), Linux de escritorio (X11 y Wayland), Linux embebido (framebuffer), iOS, Android y WebAssembly.

**Capacidades clave**:

| Capacidad | Descripción |
|---|---|
| Renderizado multiplataforma | Motor propio con Skia por defecto; colaboración con el equipo de Flutter para traer Impeller a .NET. Sin wrappers de controles nativos. |
| XAML y code-behind | UI declarativa con XAML o construida completamente en código. |
| Sistema de estilos | Inspirado en CSS: selectores, clases de estilo, pseudoclases y temas de control. |
| Data binding | Bindings compilados verificados en build time; soporte MVVM con CommunityToolkit.Mvvm. |
| Biblioteca de controles | Más de 60 controles integrados (DataGrid, TreeView, TabControl, Calendar, etc.). |
| Accesibilidad | Soporte para lectores de pantalla y navegación por teclado. |
| DevTools | F12 en runtime para inspeccionar árbol visual, propiedades, estilos y layout. |

**Plataformas soportadas (niveles)**:

Avalonia organiza el soporte en tres niveles: **Tier 1** (probado y soportado en cada release), **Tier 2** (best effort, no está en la matriz de pruebas), **Tier 3** (solo soporte comercial).

- **Windows**: Windows 11 24H2 (build 26100) x64/ARM64 es Tier 1; Windows 11 22H2 y Windows 10 22H2 x64 son Tier 2.
- **macOS**: macOS 26 Tahoe ARM64/x64 es Tier 1; macOS 15 Sequoia y 14 Sonoma son Tier 2.
- **iOS/iPadOS**: iOS 26 ARM64 es Tier 1; iOS 18.x es Tier 2. Requiere .NET 10.0 mínimo.
- **Android**: Android 16 (API 36) ARM64/x64 es Tier 1; Android 12–15 (API 31–35) es Tier 2.
- **Linux**: Ubuntu 25.x y Fedora 43 son Tier 1; rendering OpenGL vía Skia.

**Diferencias con WPF**:

| Dimensión | Avalonia | WPF |
|---|---|---|
| Plataformas | Windows, macOS, Linux, iOS, Android, WASM, embebidos | Solo Windows |
| Pipeline de renderizado | Motor vectorial propio (Skia/Impeller) | DirectX/MilCore |
| Compilación XAML | XAML → IL en build time (XamlX) | BAML |
| Estilos | Selectores tipo CSS, clases, pseudoclases | TargetType + Triggers |
| Data binding | Compilados por defecto en Avalonia 12 | Reflexión en runtime por defecto |

---

## Bloque 2: XAML y compilación

Avalonia usa la extensión `.axaml` (Avalonia XAML) para distinguir sus archivos de WPF y evitar conflictos en Visual Studio. Un archivo típico tiene esta estructura:

```xml
<Window x:Class="AvaloniaApplication1.MainWindow" xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</Window>
```

El namespace `xmlns="https://github.com/avaloniaui"` es obligatorio; sin él, el archivo no se reconoce como Avalonia XAML. `x:Class` apunta a la clase code-behind.

**Compilación XAML**:

Avalonia usa el compilador **XamlX** para procesar archivos `.axaml` en build time. A diferencia de WPF, que interpreta XAML en runtime por defecto, Avalonia compila XAML a **IL** durante la compilación. Esto proporciona arranque más rápido, binarios más pequeños y detección de errores en tiempo de compilación. Muchos errores que en WPF solo aparecerían en runtime, en Avalonia se capturan al compilar.

**Bindings compilados**:

Por defecto, los bindings usan reflexión para resolver rutas de propiedad en runtime. Los **bindings compilados** resuelven las rutas en build time y ofrecen:
- Validación en build time: errores tipográficos en nombres de propiedad causan errores de compilación.
- Mejor rendimiento: sin sobrecarga de reflexión.
- Compatibilidad con Native AOT.

Para habilitarlos por control, se usa `x:DataType` y `x:CompileBindings`:

```xml
<UserControl x:CompileBindings="True" x:DataType="vm:MainViewModel" xmlns="https://github.com/avaloniaui" xmlns:vm="using:MyApp.ViewModels" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <TextBlock Text="{Binding UserName}"/>
</UserControl>
```

Para habilitarlos en todo el proyecto, agrega en `.csproj`:

```xml
<PropertyGroup>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
</PropertyGroup>
```

Con esta configuración, todos los bindings requieren `x:DataType`. Para optar por reflexión en un binding específico, usa `{ReflectionBinding}`. En plantillas de datos se puede cambiar el tipo con `x:DataType` dentro de `<DataTemplate>`.

**Errores de compilación típicos**:

| Error | Mensaje |
|---|---|
| Typo en propiedad | `Cannot resolve property 'UserNam' on type 'MainViewModel'` |
| Falta `x:DataType` | `Cannot use compiled binding without a DataType` |
| Tipo incorrecto | `Cannot assign 'string' to property of type 'int'` |

**Marcado de propiedades y contenido**:

Se pueden usar atributos para propiedades (`<Button Content="Click me" Width="200" />`), sintaxis de elemento de propiedad para valores complejos, y la propiedad de contenido por defecto (los hijos directos se asignan a esa propiedad). Las propiedades adjuntas usan `OwnerType.PropertyName`, por ejemplo `Grid.Row="0"`. Las colecciones admiten sintaxis compacta: `<Grid ColumnDefinitions="Auto,*,200" RowDefinitions="Auto,*" />`.

**Extensiones de marcado** relevantes: `{Binding}`, `{CompiledBinding}` (equivalente a Binding con compilación explícita), `{StaticResource}`, `{DynamicResource}`, `{TemplateBinding}`.

---

## Bloque 3: Data binding y MVVM

**Bindings compilados en C#**:

Avalonia permite crear bindings programáticamente con seguridad de tipos:

```csharp
var binding = CompiledBinding.Create<UserProfileViewModel, string>(
    expression: vm => vm.UserName,
    mode: BindingMode.OneWay
);
textBlock.Bind(TextBlock.TextProperty, binding);
```

**CommunityToolkit.Mvvm** es el framework MVVM recomendado. Instalación:

```bash
dotnet add package CommunityToolkit.Mvvm
```

**Propiedades observables** con `[ObservableProperty]`: declaras un campo privado y el source generator crea la propiedad pública. La clase debe ser `partial`.

```csharp
public partial class PersonViewModel : ObservableObject
{
    [ObservableProperty]
    private string _firstName = "";
}
```

**Propiedades computadas** con `[NotifyPropertyChangedFor]`: cuando una propiedad depende de otra, se notifica automáticamente:

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(FullName))]
private string _firstName = "";

public string FullName => $"{FirstName} {LastName}";
```

**Callbacks de cambio** con métodos parciales `OnXxxChanging` (antes de asignar) y `OnXxxChanged` (después de asignar).

**Comandos** con `[RelayCommand]`: genera una propiedad `IRelayCommand` con el nombre del método + `Command`.

```csharp
[RelayCommand]
private void Save() { ... }  // genera SaveCommand
```

**Comandos con parámetro**: agrega un parámetro al método y en XAML usa `CommandParameter="{Binding SelectedItem}"`.

**Comandos asíncronos**: método `async Task`; el comando generado deshabilita el botón mientras corre, pasa `CancellationToken` y expone `IsRunning`.

```csharp
[RelayCommand]
private async Task LoadDataAsync(CancellationToken token) { ... }
```

```xml
<Button Command="{Binding LoadDataCommand}" Content="Load"/>
<ProgressBar IsIndeterminate="True" IsVisible="{Binding LoadDataCommand.IsRunning}"/>
```

**CanExecute**: usa `[RelayCommand(CanExecute = nameof(CanSave))]` y `[NotifyCanExecuteChangedFor(nameof(SaveCommand))]` en las propiedades que afectan la condición.

**Comunicación entre ViewModels**: `WeakReferenceMessenger` permite enviar mensajes sin referencias directas.

**Commanding general**: Avalonia usa `System.Windows.Input.ICommand`. Los controles con `Command` (Button, MenuItem) llaman a `Execute` al activarse y monitorean `CanExecute` para habilitarse/deshabilitarse automáticamente.

---

## Bloque 4: Estilos y selectores

Avalonia reemplaza el `TargetType` de WPF por **selectores tipo CSS** en el atributo `Selector` de `<Style>`. Los estilos se almacenan en `<Window.Styles>` (o `Application.Styles`), no en `Resources`.

**Selectores comunes**:

| Selector | Significado |
|---|---|
| `Button` | Todos los Button |
| `Button.red` | Button con clase `red` |
| `Button:focus` | Button con pseudoclase `:focus` |
| `Button#myButton` | Button con `Name="myButton"` |
| `StackPanel > Button.xl` | Button.xl hijo directo de StackPanel |
| `:is(Button)` | Button o derivados |
| `:not(Button.red)` | Que no coincidan con Button.red |
| `Button:nth-child(2n+1)` | Button en posiciones impares entre hermanos |

**Especificidad** (de mayor a menor prioridad): selectores de nombre (`#name`) > pseudoclases/propiedades (`:pointerover`) > clases (`.primary`) > tipo (`Button`). Si dos selectores empatan, gana el declarado después.

**Pseudoclases** reemplazan los Triggers de WPF: `:pointerover`, `:pressed`, `:disabled`, `:checked`, `:focus`, `:selected`, `:expanded`.

**Clases de estilo** reemplazan `x:Key`: `<Button Classes="primary large" />`, y se pueden alternar en code-behind con `myButton.Classes.Add("active")`.

**DataTrigger**: no hay equivalente directo; las alternativas son binding con converter, clase de estilo desde ViewModel/code-behind, o container queries para condiciones de tamaño.

**Container queries** reemplazan el patrón de `RelativeSource` para layouts responsivos basados en tamaño.

---

## Bloque 5: Layout y controles

**Controles de layout principales**:

| Control | Descripción |
|---|---|
| `StackPanel` | Apila hijos horizontal o verticalmente. |
| `Grid` | Celdas en filas y columnas; `ColumnDefinitions="Auto,*,200"` y `RowDefinitions="Auto,*"`. |
| `Border` | Decora un hijo con borde y fondo; soporta `CornerRadius`. |
| `DockPanel` | Ancla hijos a los bordes; uno llena el espacio restante. |
| `Canvas` | Posiciona hijos en coordenadas explícitas. |
| `ScrollViewer` | Agrega scroll si el contenido excede el espacio. |
| `WrapPanel` | Fluye hijos de izquierda a derecha, saltando de línea. |
| `UniformGrid` | Celdas de tamaño uniforme. |

En `Grid`, las celdas se numeran desde 0 y se asignan con `Grid.Row` y `Grid.Column`.

**Controles de datos repetidos**: `ItemsControl`, `ListBox`, `ComboBox`, `DataGrid` (requiere paquete aparte en Avalonia 11; en 12 se consulta la doc de controles).

**Controles de texto**: `TextBlock` (solo lectura), `TextBox` (edición), `SelectableTextBlock`, `MaskedTextBox`, `AutoCompleteBox`.

**Botones**: `Button`, `RepeatButton`, `RadioButton`, `ToggleButton`, `SplitButton`, `ToggleSplitButton`.

---

## Bloque 6: Estructura de solución multiplataforma

La plantilla **Avalonia Cross Platform Application** genera una solución con proyectos separados:

- **Core Project**: corazón de la aplicación, agnóstico de plataforma. Contiene lógica de negocio, ViewModels y Vistas. Todos los demás proyectos lo referencian. La mayor parte del desarrollo ocurre aquí.
- **Desktop Project**: permite ejecutar en Windows, macOS y Linux; output type `WinExe`.
- **Android Project**: basado en .NET-Android; `MainActivity` hereda de `AvaloniaMainActivity`.
- **iOS Project**: basado en .NET-iOS; `AppDelegate` hereda de `AvaloniaAppDelegate`.
- **Browser Project**: WebAssembly; `RuntimeIdentifier` es `browser-wasm`.

Los proyectos compartidos solo deben referenciar assemblies disponibles universalmente (`System`, `System.Core`, `System.Xml`). Los proyectos específicos de plataforma deben referenciar el Core Project.