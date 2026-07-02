using System.Text;
using Forms = System.Windows.Forms;

namespace SnipEasy.App.Services;

public sealed class EnvironmentDiagnosticsService
{
    public string BuildReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Display environment");
        builder.AppendLine($"  DPI awareness: Windows/WPF per-process defaults");
        builder.AppendLine($"  Screen count: {Forms.Screen.AllScreens.Length}");
        builder.AppendLine($"  Virtual screen: {Forms.SystemInformation.VirtualScreen}");

        foreach (var screen in Forms.Screen.AllScreens)
        {
            builder.AppendLine($"  Screen: {screen.DeviceName}");
            builder.AppendLine($"    Primary: {screen.Primary}");
            builder.AppendLine($"    Bounds: {screen.Bounds}");
            builder.AppendLine($"    Working area: {screen.WorkingArea}");
        }

        builder.AppendLine("Runtime environment");
        builder.AppendLine($"  OS: {Environment.OSVersion}");
        builder.AppendLine($"  .NET: {Environment.Version}");
        builder.AppendLine($"  Is 64-bit OS: {Environment.Is64BitOperatingSystem}");
        builder.AppendLine($"  Is 64-bit process: {Environment.Is64BitProcess}");
        builder.AppendLine($"  User interactive: {Environment.UserInteractive}");
        builder.AppendLine($"  Session name: {Environment.GetEnvironmentVariable("SESSIONNAME") ?? ""}");
        builder.AppendLine($"  Processor count: {Environment.ProcessorCount}");
        builder.AppendLine($"  System directory: {Environment.SystemDirectory}");

        builder.AppendLine("Capture risk checks");
        builder.AppendLine($"  Remote session likely: {IsRemoteSessionLikely()}");
        builder.AppendLine($"  Multiple monitors: {Forms.Screen.AllScreens.Length > 1}");
        builder.AppendLine($"  Negative virtual origin: {Forms.SystemInformation.VirtualScreen.Left < 0 || Forms.SystemInformation.VirtualScreen.Top < 0}");
        builder.AppendLine($"  High DPI needs manual UI verification: true");
        builder.AppendLine($"  Security software compatibility needs manual verification: true");
        return builder.ToString();
    }

    public bool IsRemoteSessionLikely()
    {
        var sessionName = Environment.GetEnvironmentVariable("SESSIONNAME") ?? "";
        return sessionName.Contains("RDP", StringComparison.OrdinalIgnoreCase) ||
               sessionName.Contains("Remote", StringComparison.OrdinalIgnoreCase);
    }
}
