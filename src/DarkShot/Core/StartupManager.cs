using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;

namespace DarkShot.Core;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DarkShot";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read startup setting: " + ex.Message);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not update startup setting");
            return false;
        }
    }

    public static string BuildCommand()
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        if (exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(assembly))
                return $"\"{exe}\" \"{assembly}\" --startup";
        }
        return $"\"{exe}\" --startup";
    }

    public static bool MatchesCurrentExecutable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Contains(Path.GetFileName(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
