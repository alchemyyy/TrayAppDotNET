using System.Xml.Serialization;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>
/// Task Manager's <c>theme.xml</c> root. Task Manager adds no theme colors of its own, but the generated XML
/// serializer only exists for <c>[XmlRoot]</c> types, so without this type a user's <c>theme.xml</c> never loads.
/// </summary>
[XmlRoot("Theme")]
public sealed class TaskManagerAppTheme : AppTheme;

internal static class AppThemeStore
{
    public static string GetDefaultPath()
    {
        string appDirectory = AppSettings.GetDefaultDirectory();
        Directory.CreateDirectory(appDirectory);
        return Path.Combine(appDirectory, path2: "theme.xml");
    }

    public static AppTheme LoadOrDefault(string path) =>
        AppTheme.LoadOrDefault<TaskManagerAppTheme>(
            path,
            static message => TADNLog.Log($"AppThemeStore.LoadOrDefault: {message}"));
}
