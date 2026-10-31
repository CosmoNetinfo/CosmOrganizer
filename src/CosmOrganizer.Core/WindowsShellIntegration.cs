// CosmOrganizer.Core - WindowsShellIntegration
// Explorer context menu integration under HKCU (no administrator rights needed).
// Adds "Organizza con CosmOrganizer" to folder right-click menus.
// © CosmoNet.info — MIT License

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CosmOrganizer.Core;

public static class WindowsShellIntegration
{
    private const string FolderShellKey = @"Software\Classes\Directory\shell\CosmOrganizer";
    private const string BackgroundShellKey = @"Software\Classes\Directory\Background\shell\CosmOrganizer";

    public static bool IsContextMenuRegistered()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FolderShellKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static void RegisterContextMenu(string exePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            // 1. Directory context menu (right click on a folder)
            using (var key = Registry.CurrentUser.CreateSubKey(FolderShellKey))
            {
                key?.SetValue("", "Organizza con CosmOrganizer");
                key?.SetValue("Icon", $"\"{exePath}\"");
                using var cmdKey = key?.CreateSubKey("command");
                cmdKey?.SetValue("", $"\"{exePath}\" --organize \"%1\"");
            }

            // 2. Directory background context menu (right click inside a folder)
            using (var bgKey = Registry.CurrentUser.CreateSubKey(BackgroundShellKey))
            {
                bgKey?.SetValue("", "Organizza questa cartella con CosmOrganizer");
                bgKey?.SetValue("Icon", $"\"{exePath}\"");
                using var cmdKey = bgKey?.CreateSubKey("command");
                cmdKey?.SetValue("", $"\"{exePath}\" --organize \"%V\"");
            }
        }
        catch { }
    }

    public static void UnregisterContextMenu()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\CosmOrganizer", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\CosmOrganizer", throwOnMissingSubKey: false);
        }
        catch { }
    }
}
