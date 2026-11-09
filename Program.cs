using Avalonia;
using System.Text;
namespace HoursTool;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // nutne pro ExcelDataReader na .NET 5+ (kodova stranka 1252)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
