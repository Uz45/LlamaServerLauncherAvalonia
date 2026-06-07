using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
#pragma warning disable CA1416
using LlamaServerLauncher.Models;

namespace LlamaServerLauncher.Services;

public class AutoStartService
{
    private const string AppName = "LlamaServerLauncher";
    private readonly LogService _logService;

    public AutoStartService(LogService logService)
    {
        _logService = logService;
    }

    public bool IsAutoStartEnabled()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return IsWindowsAutoStartEnabled();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return IsLinuxAutoStartEnabled();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return IsMacOSAutoStartEnabled();
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to check auto-start state: {ex.Message}");
        }

        return false;
    }

    public void SetAutoStart(bool enabled)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                SetWindowsAutoStart(enabled);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                SetLinuxAutoStart(enabled);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                SetMacOSAutoStart(enabled);
            else
                _logService.Warning("Auto-start is not supported on this platform");
        }
        catch (Exception ex)
        {
            _logService.Error($"Failed to set auto-start: {ex.Message}");
        }
    }

    private string GetExecutablePath()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainModule?.FileName ?? Environment.GetCommandLineArgs()[0];
    }

    #region Windows

    private bool IsWindowsAutoStartEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
        return key?.GetValue(AppName) != null;
    }

    private void SetWindowsAutoStart(bool enabled)
    {
        if (enabled)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null)
            {
                _logService.Error("Failed to open Windows Run registry key");
                return;
            }

            var exePath = GetExecutablePath();
            key.SetValue(AppName, $"\"{exePath}\"");
            _logService.Info("Auto-start enabled (Windows registry)");
        }
        else
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key?.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName);
                _logService.Info("Auto-start disabled (Windows registry)");
            }
        }
    }

    #endregion

    #region Linux

    private static string GetLinuxDesktopFilePath()
    {
        var autostartDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "autostart");
        return Path.Combine(autostartDir, "llama-server-launcher.desktop");
    }

    private bool IsLinuxAutoStartEnabled()
    {
        var path = GetLinuxDesktopFilePath();
        return File.Exists(path);
    }

    private void SetLinuxAutoStart(bool enabled)
    {
        var desktopFile = GetLinuxDesktopFilePath();
        var autostartDir = Path.GetDirectoryName(desktopFile)!;

        if (enabled)
        {
            Directory.CreateDirectory(autostartDir);
            var exePath = GetExecutablePath();
            var content = $@"[Desktop Entry]
Type=Application
Name={AppName}
Exec=""{exePath}""
Terminal=false
Hidden=false
";
            File.WriteAllText(desktopFile, content);

            try
            {
                var chmod = Process.Start("chmod", $"+x \"{desktopFile}\"");
                chmod?.WaitForExit(3000);
            }
            catch
            {
            }

            _logService.Info("Auto-start enabled (Linux desktop file)");
        }
        else
        {
            if (File.Exists(desktopFile))
            {
                File.Delete(desktopFile);
                _logService.Info("Auto-start disabled (Linux desktop file)");
            }
        }
    }

    #endregion

    #region macOS

    private static string GetMacOSPlistPath()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, "Library", "LaunchAgents", "org.llamaserverlauncher.autostart.plist");
    }

    private bool IsMacOSAutoStartEnabled()
    {
        return File.Exists(GetMacOSPlistPath());
    }

    private void SetMacOSAutoStart(bool enabled)
    {
        var plistPath = GetMacOSPlistPath();
        var agentsDir = Path.GetDirectoryName(plistPath)!;

        if (enabled)
        {
            Directory.CreateDirectory(agentsDir);
            var exePath = GetExecutablePath();
            var label = "org.llamaserverlauncher.autostart";

            var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>{label}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{exePath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>";
            File.WriteAllText(plistPath, plistContent);
            _logService.Info("Auto-start enabled (macOS LaunchAgent)");
        }
        else
        {
            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
                _logService.Info("Auto-start disabled (macOS LaunchAgent)");
            }
        }
    }

    #endregion
}
#pragma warning restore CA1416
