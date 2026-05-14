using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using D2Helper.UI.ViewModels;
using D2Helper.UI.Views;
using D2Helper.Vision;

namespace D2Helper.UI;

public partial class App : Application
{
    private VisionLoop? _vision;
    private MinimapOverlayViewModel? _visionVm;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Прозорий overlay поверх Dota з тими ж активними квестами.
            var overlay = new OverlayWindow { DataContext = vm };
            overlay.Show();

            // Vision (Phase V1): захоплення мінімапи + fog-маска. Лише на Windows.
            if (OperatingSystem.IsWindows())
            {
                _vision = new VisionLoop();
                _vision.Start();
                _visionVm = new MinimapOverlayViewModel(_vision, vm.GameStateBus);
                var visionWin = new MinimapOverlayWindow { DataContext = _visionVm };
                visionWin.Show();

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
