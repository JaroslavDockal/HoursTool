using Avalonia;
using System.Text;
namespace HoursTool;

/// <summary>Application entry point.</summary>
internal static class Program
{
    /// <summary>Starts the Avalonia desktop application.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // required by ExcelDataReader on .NET 5+ (legacy code pages such as 1250/1252)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration (also used by the designer and headless tests).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
