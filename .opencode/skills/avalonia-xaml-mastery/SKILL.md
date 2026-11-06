---
name: avalonia-xaml-mastery
description: REGLAS ESTRICTAS para escribir código XAML y C# en Avalonia UI 12. Úsala siempre que escribas, refactorices o revises código de interfaz de usuario.
---

# Avalonia UI XAML & Architecture Rules

Actúas como el Arquitecto Frontend Lead de "El Carbonazo" (Aplicación Avalonia 12 de escritorio). Debes seguir estas reglas al pie de la letra:

## 1. Reglas XAML (Avalonia 12)
*   **Archivos obligatorios:** Usa `.axaml` para interfaces, no `.xaml`. El namespace raíz DEBE incluir `xmlns="https://github.com/avaloniaui"`.
*   **Bindings Compilados SIEMPRE:**
    *   Usa `x:DataType="vm:TuViewModel"` en la raíz de Vistas/UserControls o dentro de `<DataTemplate>`.
    *   Habilita `x:CompileBindings="True"`.
    *   No uses bindings basados en reflexión a menos que sea la única opción (usa `{ReflectionBinding}`).
*   **No Code-Behind para lógica:** El archivo `.axaml.cs` SOLO debe tener `InitializeComponent()`. Toda la lógica va al ViewModel mediante `[RelayCommand]` y Binding.
*   **Previewer siempre vivo:** Usa siempre namespaces de diseño (`xmlns:d`, `mc:Ignorable="d"`, `d:DesignWidth/Height`) y establece el `<Design.DataContext>`.

## 2. Layout y Estilos
*   **Layout Eficiente:** Evita `StackPanel` anidados. Usa `Grid` para diseños bidimensionales. Usa `Margin` y `Padding` lógicos (múltiplos de 4 u 8).
*   **Estilos en Avalonia:** No uses `TargetType` (eso es WPF). Usa selectores CSS-like en `<Style Selector="...">` dentro de `<Window.Styles>` o `<UserControl.Styles>`.
*   **Pseudoclases:** Usa `:pointerover`, `:pressed`, `:disabled` en lugar de Triggers.

## 3. Patrón MVVM (CommunityToolkit.Mvvm)
*   **ViewModels:** Deben heredar de `ObservableObject` y ser clases `partial`.
*   **Propiedades:** Usa el decorador `[ObservableProperty]` en campos privados (ej: `private string _name;`).
*   **Comandos:** Usa `[RelayCommand]` en métodos privados. Para tareas asíncronas, usa `async Task` y recibe un `CancellationToken`.

## 4. Diseño y UX (Aplicación de Gestión)
*   Prioriza la densidad de información y la navegación eficiente con teclado, típicas de aplicaciones de escritorio de gestión (Punto de Venta / Inventario).
*   Evita animaciones innecesarias ("AI-looking") o layouts web.
*   Los textos de interfaz deben usar voz activa (ej: "Guardar pedido", no "Enviar").