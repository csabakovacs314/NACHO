using System.ComponentModel;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Nacho.Core;

namespace Nacho.Linux;

/// <summary>Live language switch: XAML binds to Loc.Instance[key]; switching raises a change for every key.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private IReadOnlyDictionary<string, string> _dict = Strings.ForPlatform("en", linux: true);

    public string Language { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = Strings.CultureFor("en");

    public string this[string key] =>
        _dict.TryGetValue(key, out var v) ? v : Strings.En.TryGetValue(key, out var e) ? e : key;

    public static string T(string key) => Instance[key];
    public static string F(string key, params object[] args) => string.Format(Instance.Culture, Instance[key], args);
    public static string Bytes(long bytes) => Strings.Bytes(bytes, Instance.Culture);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public void Set(string language)
    {
        Language = Strings.Normalise(language);
        _dict = Strings.ForPlatform(Language, linux: true);
        Culture = Strings.CultureFor(Language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        Changed?.Invoke();
    }
}

/// <summary>{l:L Key} in XAML: a one-way binding to the current language's text.</summary>
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }
    public LExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBinding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
