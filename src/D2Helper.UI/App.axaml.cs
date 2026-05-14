using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using D2Helper.UI.ViewModels;
using D2Helper.UI.Views;

namespace D2Helper.UI;

public partial class App : Application
{
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
        }
        base.OnFrameworkInitializationCompleted();
    }
}
