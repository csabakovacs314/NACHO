using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nacho;

/// <summary>The right-click entry (current user only, no admin) and the Send to shortcut.</summary>
public static class ShellIntegration
{
    private const string Verb = "NACHO.MakeDocsReady";

    private static readonly string[] Bases =
    {
        @"SystemFileAssociations\image",
        @"SystemFileAssociations\.heic",
        @"SystemFileAssociations\.heif",
        @"SystemFileAssociations\.webp",
        @"SystemFileAssociations\.avif",
        @"SystemFileAssociations\.docx",
    };

    public static string SendToShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "NACHO.lnk");

    /// <summary>True when the right-click entry exists, whoever made it (NACHO's options or the installer).</summary>
    public static bool IsContextMenuOn()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Bases[0]}\shell\{Verb}\command");
            return k?.GetValue("") is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when the Send to shortcut exists (the installer and NACHO use the same name).</summary>
    public static bool IsSendToOn() => File.Exists(SendToShortcut);

    public static void Apply(bool contextMenu, bool sendTo, string menuText)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where NACHO.exe is.");

        foreach (string b in Bases)
        {
            string key = $@"Software\Classes\{b}\shell\{Verb}";
            if (contextMenu)
            {
                using var k = Registry.CurrentUser.CreateSubKey(key);
                k.SetValue("", menuText);
                k.SetValue("MUIVerb", menuText);
                k.SetValue("Icon", $"\"{exe}\",0");
                k.SetValue("MultiSelectModel", "Player");
                using var c = k.CreateSubKey("command");
                c.SetValue("", $"\"{exe}\" \"%1\"");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            }
        }

        if (sendTo) CreateShortcut(SendToShortcut, exe);
        else if (File.Exists(SendToShortcut)) File.Delete(SendToShortcut);
    }

    private static void CreateShortcut(string lnk, string exe)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        object shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic sh = shell;
            dynamic sc = sh.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.IconLocation = exe + ",0";
            sc.Description = "NACHO";
            sc.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
