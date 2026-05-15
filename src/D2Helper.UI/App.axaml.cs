using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using D2Helper.UI.ViewModels;
using D2Helper.UI.Views;
using D2Helper.Vision;
using System.Runtime.InteropServices;

namespace D2Helper.UI;

public partial class App : Application
{
    private VisionLoop? _vision;
    private MinimapOverlayViewModel? _visionVm;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            // У Debug-білді відкриваємо консольне вікно для логів (WinExe ховає stdout).
            if (OperatingSystem.IsWindows())
            {
                AllocConsole();
                Console.WriteLine("=== D2Helper DEBUG console ===");
            }
#endif
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Прозорий overlay поверх Dota з тими ж активними квестами.
            var overlay = new OverlayWindow { DataContext = vm };
            overlay.Show();

            // Vision (Phase V1.1): danger-heatmap поверх ігрової мінімапи + опційний debug-widget.
            if (OperatingSystem.IsWindows())
            {
                _vision = new VisionLoop();
                _vision.Start();

                // In-game heatmap overlay — головний продукт (transparent, click-through, no chrome).
                var heatmapWin = new DangerHeatmapWindow(_vision, vm.GameStateBus);
                heatmapWin.Show();

                // Debug-widget (захоплена мінімапа + fog-маска) — прихований за замовчуванням,
                // Alt+F10 розкриває. Корисний для калібрування і верифікації.
                _visionVm = new MinimapOverlayViewModel(_vision, vm.GameStateBus);
                var visionWin = new MinimapOverlayWindow { DataContext = _visionVm };
                visionWin.Show();
                visionWin.Opacity = 0; // ховаємо одразу після Show — інакше hotkey-watcher не стартує.

                desktop.ShutdownRequested += (_, _) =>
                {
                    _visionVm?.Dispose();
                    _vision?.Dispose();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
