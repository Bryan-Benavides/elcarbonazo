---
name: sukiui-implementation
description: REGLAS y COMPONENTES de diseño usando SukiUI 7.0.1. Úsala para construir pantallas, manejar navegación lateral, diálogos, notificaciones o temas en Avalonia 12.
---

# SukiUI 7.0.1 Implementation Guidelines

Estás implementando la interfaz para Avalonia 12 utilizando el framework de diseño **SukiUI**. NO intentes inventar componentes personalizados complejos si SukiUI ya los provee.

## 1. Ventana Principal y Tema
*   **Ventana raíz:** La aplicación DEBE usar `sukiUi:SukiWindow`, no el `Window` estándar. (Requiere namespace `xmlns:sukiUi="clr-namespace:SukiUI.Controls;assembly=SukiUI"`).
*   **Tema Global:** Los colores no se hardcodean. Confía en el motor de SukiUI. El tema se inicializa en `App.axaml` con `<sukiUi:SukiTheme ThemeColor="Blue" />`.
*   **Fondos:** Usa `BackgroundStyle` (`Bubble`, `Gradient`, `Flat`) en `SukiWindow`.

## 2. Controles Especializados (¡Úsalos!)
*   **Contenedores:** En lugar de `Border` con sombras personalizadas, usa `<suki:GlassCard>` para agrupar formularios o métricas con efecto translúcido.
*   **Layout de Ajustes:** Usa `<suki:SettingsLayout>` para pantallas de configuración.
*   **Carga y Progreso:** Usa `<suki:BusyArea>` para bloquear pantallas durante operaciones asíncronas. Usa `<suki:WaveProgress>` o `<suki:Stepper>` para estados visuales.
*   **Alertas in-page:** Usa `<suki:InfoBar>`.

## 3. Navegación Principal
*   Usa `<suki:SukiSideMenu>` para la estructura principal ("Ventas", "Inventario", etc.).
*   **Regla Crítica:** Cada `<suki:SukiSideMenuItem>` terminal (hoja) DEBE contener un `<suki:SukiSideMenuItem.PageContent>`. No lo dejes vacío o la app crasheará.

## 4. Diálogos y Notificaciones (Hosts)
*   Nunca uses ventanas modales nativas (`Window.ShowDialog()`).
*   **Toast:** Usa `ISukiToastManager` desde el ViewModel. Ejemplo: `ToastManager.CreateToast().WithTitle("Éxito").AsSuccess().Queue();`
*   **Diálogos:** Usa `ISukiDialogManager` desde el ViewModel para modales informativos o de confirmación integrados en el framework.