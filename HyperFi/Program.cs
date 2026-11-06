using Avalonia;
using Avalonia.Media;

namespace HyperFi;

/// <summary>Configures and starts the HyperFi Avalonia desktop application.</summary>
sealed class Program
{
    /// <summary>Starts the classic desktop lifetime.</summary>
    /// <param name="args">Command-line arguments passed to Avalonia.</param>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Builds the platform-configured Avalonia application.</summary>
    /// <returns>The configured application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "Noto Serif CJK SC",
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily("Noto Serif CJK SC") },
                    new FontFallback { FontFamily = new FontFamily("Noto Serif SC") },
                    new FontFallback { FontFamily = new FontFamily("Noto Serif") }
                ]
            });

        if (OperatingSystem.IsMacOS())
        {
            builder = builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Software
                ]
            });
        }

        return builder.LogToTrace();
    }
}
