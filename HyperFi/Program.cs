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
                // The bundled Noto Serif SC family is exposed under an app-owned
                // alias so the embedded files always win over a system install.
                DefaultFamilyName = "HyperFi Serif",
                FontFamilyMappings = new Dictionary<string, FontFamily>
                {
                    ["HyperFi Serif"] =
                        new("avares://HyperFi/Assets/Fonts#Noto Serif SC"),
                    ["HyperFi Serif Devanagari"] =
                        new("avares://HyperFi/Assets/Fonts#Noto Serif Devanagari")
                },
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily("HyperFi Serif Devanagari") },
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
