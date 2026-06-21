using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Services;

public static class ApplicationRestartService
{
    public static void Restart()
    {
        var executablePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Log.Error("Restart failed: executable path is empty.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Arguments = BuildArguments()
            });

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }
        catch (Exception e)
        {
            Log.Error("Restart failed: " + e.Message);
        }
    }

    public static bool TryRestartAsAdministrator(string[]? args = null)
    {
        if (!OperatingSystem.IsWindows() || IsRunningAsAdministrator())
        {
            return false;
        }

        var executablePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Log.Warning("Administrator relaunch skipped: executable path is empty.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = BuildArguments(args ?? Environment.GetCommandLineArgs().Skip(1).ToArray())
            });
            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Log.Warning("Administrator relaunch was cancelled by user. Fullscreen game input support may be limited.");
        }
        catch (Exception e)
        {
            Log.Warning("Administrator relaunch failed: " + e.Message);
        }

        return false;
    }

    public static bool IsRunningAsAdministrator()
    {
        return OperatingSystem.IsWindows() && IsRunningAsAdministratorCore();
    }

    private static string? GetExecutablePath()
    {
        return Environment.ProcessPath ??
               Process.GetCurrentProcess().MainModule?.FileName;
    }

    private static string BuildArguments()
    {
        return string.Join(" ", Environment.GetCommandLineArgs()
            .Skip(1)
            .Select(QuoteArgument));
    }

    private static string BuildArguments(string[] args)
    {
        return string.Join(" ", args.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument)) return "\"\"";
        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;
        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdministratorCore()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
