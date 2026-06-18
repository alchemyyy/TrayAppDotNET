using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TrayAppDotNETCommon;

public static class TrayAppDotNETRunner
{
    public static int Run(string[] args, TrayAppDotNETHostOptions options)
    {
        TrayAppDotNETApplication.Options = options;
        return AppBuilder.Configure<TrayAppDotNETApplication>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = "Segoe UI", })
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
}
