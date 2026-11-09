using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace HoursTool;

/// <summary>Avalonia application: loads styles (App.axaml) and opens the main window.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
