using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using PBLApp.Core.Localization;

namespace PBLApp.Localization;

/// <summary>
/// XAML markup extension for localized strings.
/// Usage: Text="{loc:Tr Btn_Save}"
///
/// Returns a Binding to LocalizationService.CurrentLanguage with a converter that
/// resolves the Key. When SetLanguage fires PropertyChanged(CurrentLanguage),
/// the binding re-evaluates and the converter looks up the new translation.
///
/// We can't use indexer binding ("[Key]") here because Avalonia 12 doesn't
/// refresh indexer-path bindings on PropertyChanged("Item[]") — language
/// changes would leave the UI stuck on the initial language.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider _) =>
        new Binding(nameof(LocalizationService.CurrentLanguage))
        {
            Source    = LocalizationService.Instance,
            Converter = new TrConverter(Key),
            Mode      = BindingMode.OneWay,
        };

    private sealed class TrConverter(string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => LocalizationService.Get(key);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
