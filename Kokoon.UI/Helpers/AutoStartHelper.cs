using Microsoft.Win32;

namespace Kokoon.UI.Helpers;

/// <summary>
/// Registers or unregisters Kokoon Downloader to launch automatically at
/// Windows sign-in via the current user's Run registry key. No admin
/// privileges required (HKCU, not HKLM).
/// </summary>
public static class AutoStartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KokoonDownloader";

    /// <summary>Adds or removes the startup registry entry to match <paramref name="enabled"/>.</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort — registry access may be restricted in some environments.
        }
    }
}
