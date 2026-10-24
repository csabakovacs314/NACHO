using System.Text.RegularExpressions;
using Nacho.Core;
using Nacho.Core.Linux;
using Xunit;

namespace Nacho.Tests;

/// <summary>The Linux texts, XDG folders and desktop files. These run on Windows and Linux.</summary>
public sealed class LinuxTextTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}");

    [Fact]
    public void Linux_lists_have_the_same_keys_and_only_known_keys()
    {
        Assert.Equal(Strings.LinuxEn.Keys.OrderBy(k => k), Strings.LinuxEs.Keys.OrderBy(k => k));
        Assert.Equal(Strings.LinuxEn.Keys.OrderBy(k => k), Strings.LinuxHu.Keys.OrderBy(k => k));
        Assert.All(Strings.LinuxEn.Keys, k => Assert.True(Strings.En.ContainsKey(k), k));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("hu")]
    public void Linux_placeholders_match_the_shared_text(string lang)
    {
        foreach (var (key, text) in Strings.LinuxFor(lang))
        {
            var a = Placeholder.Matches(Strings.En[key]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var b = Placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), $"{lang}: {key}");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("hu")]
    public void Linux_texts_never_name_Windows_things(string lang)
    {
        var merged = Strings.ForPlatform(lang, linux: true);
        Assert.Equal(Strings.En.Count, merged.Count);
        foreach (var (key, text) in merged)
        {
            foreach (string word in new[] { "Explorer", "Explorador", "Fájlkezelő", "Win+", "Windows", "NACHO.exe", "Send to", "Enviar a", "Küldés" })
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{lang} {key} names {word}: {text}");
            }
            Assert.DoesNotContain('\u2014', text);
            Assert.DoesNotContain('\u2013', text);
        }
    }

    [Fact]
    public void Windows_texts_are_untouched()
    {
        Assert.Same(Strings.En, Strings.ForPlatform("en", linux: false));
        Assert.Contains("Explorer", Strings.ForPlatform("en", linux: false)["Drop.Sub"], StringComparison.Ordinal);
        Assert.Contains("Open With", Strings.ForPlatform("en", linux: true)["Drop.Sub"], StringComparison.Ordinal);
    }
}

public sealed class XdgPathsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "nacho-xdg-" + Guid.NewGuid().ToString("N"));

    public XdgPathsTests() => Directory.CreateDirectory(_home);

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private XdgPaths With(params (string Name, string Value)[] vars)
    {
        var all = new Dictionary<string, string> { ["HOME"] = _home };
        foreach (var (n, v) in vars) all[n] = v;
        return new XdgPaths(n => all.TryGetValue(n, out var v) ? v : null);
    }

    [Fact]
    public void Defaults_follow_the_spec()
    {
        var x = With();
        Assert.Equal(Path.Combine(_home, ".config"), x.ConfigHome);
        Assert.Equal(Path.Combine(_home, ".local", "share"), x.DataHome);
        Assert.Equal(Path.Combine(_home, ".config", "nacho", "settings.json"), x.SettingsFile);
        Assert.Equal(Path.Combine(_home, "Pictures"), x.Pictures);
    }

    [Fact]
    public void Absolute_xdg_variables_win_and_relative_ones_are_ignored()
    {
        Assert.Equal("/srv/cfg", With(("XDG_CONFIG_HOME", "/srv/cfg")).ConfigHome);
        Assert.Equal("/srv/data", With(("XDG_DATA_HOME", "/srv/data")).DataHome);
        Assert.Equal(Path.Combine(_home, ".config"), With(("XDG_CONFIG_HOME", "relative/cfg")).ConfigHome);
    }

    [Fact]
    public void Pictures_comes_from_user_dirs_even_when_translated()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".config"));
        File.WriteAllText(Path.Combine(_home, ".config", "user-dirs.dirs"),
            "# comment\nXDG_DESKTOP_DIR=\"$HOME/Asztal\"\nXDG_PICTURES_DIR=\"$HOME/Képek\"\n");
        Assert.Equal(_home + "/Képek", With().Pictures);
    }

    [Fact]
    public void Pictures_set_to_home_means_not_set()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".config"));
        File.WriteAllText(Path.Combine(_home, ".config", "user-dirs.dirs"), "XDG_PICTURES_DIR=\"$HOME/\"\n");
        Assert.Equal(Path.Combine(_home, "Pictures"), With().Pictures);
    }

    [Theory]
    [InlineData("LANG", "hu_HU.UTF-8", "hu")]
    [InlineData("LANG", "es_ES.UTF-8", "es")]
    [InlineData("LANG", "de_DE.UTF-8", "en")]
    [InlineData("LANG", "C.UTF-8", "en")]
    [InlineData("LANGUAGE", "es:en", "es")]
    [InlineData("LC_ALL", "hu_HU", "hu")]
    public void Ui_language_comes_from_the_locale(string name, string value, string expected)
    {
        Assert.Equal(expected, With((name, value)).UiLanguage);
    }
}

public sealed class DesktopFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nacho-desk-" + Guid.NewGuid().ToString("N"));
    private readonly DesktopTargets _t;
    private static readonly byte[] Svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

    public DesktopFilesTests()
    {
        _t = new DesktopTargets(Path.Combine(_root, "data"), Path.Combine(_root, "config"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("/opt/nacho/nacho", "\"/opt/nacho/nacho\"")]
    [InlineData("/home/a b/NACHO $1/nacho", "\"/home/a b/NACHO \\\\$1/nacho\"")]
    [InlineData("/home/x/50%/nacho", "\"/home/x/50%%/nacho\"")]
    [InlineData("/home/x/say \"hi\"/nacho", "\"/home/x/say \\\\\"hi\\\\\"/nacho\"")]
    public void Exec_arguments_follow_the_desktop_entry_spec(string path, string expected)
    {
        Assert.Equal(expected, DesktopFiles.ExecArg(path));
    }

    [Fact]
    public void Shell_arguments_survive_single_quotes()
    {
        Assert.Equal("'/home/o'\\''brien/nacho'", DesktopFiles.ShellArg("/home/o'brien/nacho"));
    }

    [Fact]
    public void App_entry_is_a_valid_desktop_file_with_open_with()
    {
        string text = DesktopFiles.AppEntryText("/home/u/Apps/NACHO-0.4.0-x86_64.AppImage");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.All(lines.Skip(1), l => Assert.True(l.StartsWith('#') || Regex.IsMatch(l, @"^[A-Za-z0-9-]+(\[[a-z]{2}\])?="), l));
        Assert.Contains("Exec=\"/home/u/Apps/NACHO-0.4.0-x86_64.AppImage\" %F", lines);
        Assert.Contains("Comment[hu]=Normalizál, Arányt tart, Csökkent és Hűen Odaad", lines);
        Assert.Contains(lines, l => l.StartsWith("MimeType=image/jpeg;", StringComparison.Ordinal) && l.Contains("image/heic;", StringComparison.Ordinal));
        Assert.DoesNotContain(text, c => c == '\r');
    }

    [Fact]
    public void Turning_both_on_writes_every_file_with_the_right_modes()
    {
        var written = DesktopFiles.Apply(_t, contextMenu: true, appMenu: true, "/opt/nacho/nacho", "Make Docs-ready", Svg, Png);

        Assert.True(DesktopFiles.IsContextMenuOn(_t));
        Assert.True(DesktopFiles.IsAppMenuOn(_t));
        Assert.Equal("/opt/nacho/nacho", DesktopFiles.AppEntryExec(_t));
        string nautilus = Path.Combine(_t.NautilusScripts, "Make Docs-ready");
        string caja = Path.Combine(_t.CajaScripts, "Make Docs-ready");
        foreach (string f in new[] { nautilus, caja, _t.NemoAction, _t.DolphinMenu, _t.DolphinMenu5, _t.AppEntry, _t.IconSvg, _t.IconPng })
        {
            Assert.True(File.Exists(f), f);
            Assert.Contains(f, written);
        }
        Assert.Contains("exec '/opt/nacho/nacho' \"$@\"", File.ReadAllText(nautilus), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(nautilus).HasFlag(UnixFileMode.UserExecute), "Nautilus script is executable");
            Assert.True(File.GetUnixFileMode(_t.DolphinMenu).HasFlag(UnixFileMode.UserExecute), "Dolphin 6 menu is executable");
        }
        Assert.Contains("Name[es]=Preparar para Docs", File.ReadAllText(_t.DolphinMenu), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_language_replaces_the_script_instead_of_adding_one()
    {
        DesktopFiles.Apply(_t, true, false, "/opt/nacho/nacho", "Make Docs-ready", Svg, Png);
        DesktopFiles.Apply(_t, true, false, "/opt/nacho/nacho", "Docs-kész másolat készítése", Svg, Png);
        var scripts = Directory.GetFiles(_t.NautilusScripts).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "Docs-kész másolat készítése" }, scripts);
        Assert.Contains("Name=Docs-kész másolat készítése", File.ReadAllText(_t.NemoAction), StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_off_removes_only_nachos_files()
    {
        Directory.CreateDirectory(_t.NautilusScripts);
        string mine = Path.Combine(_t.NautilusScripts, "My own script");
        File.WriteAllText(mine, "#!/bin/sh\necho hi\n");
        Directory.CreateDirectory(Path.GetDirectoryName(_t.DolphinMenu)!);
        string foreign = Path.Combine(Path.GetDirectoryName(_t.DolphinMenu)!, "other.desktop");
        File.WriteAllText(foreign, "[Desktop Entry]\nType=Service\n");

        DesktopFiles.Apply(_t, true, true, "/opt/nacho/nacho", "Make Docs-ready", Svg, Png);
        DesktopFiles.RemoveAll(_t);

        Assert.False(DesktopFiles.IsContextMenuOn(_t));
        Assert.False(DesktopFiles.IsAppMenuOn(_t));
        foreach (string f in new[] { _t.NemoAction, _t.DolphinMenu, _t.DolphinMenu5, _t.AppEntry, _t.IconSvg, _t.IconPng })
        {
            Assert.False(File.Exists(f), f);
        }
        Assert.True(File.Exists(mine), "the user's own script stays");
        Assert.True(File.Exists(foreign), "another app's service menu stays");
    }

    [Fact]
    public void An_app_entry_nacho_did_not_write_is_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_t.AppEntry)!);
        File.WriteAllText(_t.AppEntry, "[Desktop Entry]\nName=Someone else's nacho\n");
        DesktopFiles.RemoveAll(_t);
        Assert.True(File.Exists(_t.AppEntry));
        Assert.False(DesktopFiles.IsAppMenuOn(_t));
    }

    [Theory]
    [InlineData("Make Docs-ready", "Make Docs-ready")]
    [InlineData("a/b", "ab")]
    [InlineData("..hidden", "hidden")]
    [InlineData("  ", "NACHO")]
    public void Script_names_are_safe_file_names(string menuText, string expected)
    {
        Assert.Equal(expected, DesktopFiles.ScriptName(menuText));
    }
}
