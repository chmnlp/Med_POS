using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace BillKro.Desktop;

/// <summary>
/// Re-colours the Fluent theme with the BillKro Rx brand (teal) instead of the Windows accent colour.
/// </summary>
/// <remarks>
/// Fluent bakes the system accent into ~70 brushes (buttons, focus lines, check boxes, selection...).
/// Overriding the SystemAccentColor keys has no effect, so after each theme change we scan the active
/// Fluent dictionary, clone every brush that uses a stock accent colour with the brand colour swapped in,
/// and merge those clones after the theme so they win the lookup.
/// </remarks>
public static class BrandTheme
{
    // Stock Fluent accent shades (light theme, then dark theme) mapped to brand teal shades.
    private static readonly Dictionary<Color, Color> LightMap = new()
    {
        [Hex("#0067C0")] = Hex("#0F766E"),
        [Hex("#003E92")] = Hex("#115E59"),
        [Hex("#001A68")] = Hex("#134E4A"),
        [Hex("#0078D4")] = Hex("#0D9488"),
    };

    private static readonly Dictionary<Color, Color> DarkMap = new()
    {
        [Hex("#4CC2FF")] = Hex("#2DD4BF"),
        [Hex("#99EBFF")] = Hex("#99F6E4"),
        [Hex("#0078D4")] = Hex("#0D9488"),
    };

    private static readonly Lazy<ControlTemplate> CentredCellTemplate = new(() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate TargetType="DataGridCell" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <Border BorderThickness="{TemplateBinding BorderThickness}" BorderBrush="{TemplateBinding BorderBrush}"
                    Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
                <ContentPresenter VerticalAlignment="Center" SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}" />
            </Border>
        </ControlTemplate>
        """));

    private static ResourceDictionary? _overrides;
    private static bool _watchingSystem;

    public static bool IsDark { get; private set; }

    /// <summary>Raised after the palette changes, e.g. for views that draw with theme-dependent colours.</summary>
    public static event Action? Changed;

    public static void Apply(string theme)
    {
        var app = Application.Current;
        app.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        IsDark = theme == "Dark" || (theme != "Light" && IsSystemDark());
        Recolour(app);

        if (!_watchingSystem)
        {
            _watchingSystem = true;
            // In System mode Fluent follows the Windows setting on its own; keep the brand colours in step.
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                app.Dispatcher.BeginInvoke(() =>
                {
                    if (app.ThemeMode != ThemeMode.System) return;
                    IsDark = IsSystemDark();
                    Recolour(app);
                });
            };
        }
    }

    public static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static void Recolour(Application app)
    {
        var merged = app.Resources.MergedDictionaries;
        if (_overrides is not null) merged.Remove(_overrides);

        var fluent = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("PresentationFramework.Fluent") == true);
        if (fluent is null) return;

        var map = IsDark ? DarkMap : LightMap;
        var overrides = new ResourceDictionary();
        foreach (var (key, value) in Flatten(fluent))
        {
            var swapped = value switch
            {
                SolidColorBrush b when map.TryGetValue(b.Color, out var c) => Freeze(new SolidColorBrush(c) { Opacity = b.Opacity }),
                GradientBrush g when g.GradientStops.Any(s => map.ContainsKey(s.Color)) => Swap(g, map),
                _ => null,
            };
            if (swapped is not null) overrides[key] = swapped;
        }

        // Selected grid rows: a soft tint keeps secondary text and inline editors readable (solid accent did not).
        overrides["DataGridRowSelectedBackgroundThemeBrush"] = Freeze(new SolidColorBrush(Hex(IsDark ? "#1E4A46" : "#D3F2EC")));
        overrides["DataGridRowSelectedForegroundThemeBrush"] = Freeze(new SolidColorBrush(IsDark ? Colors.White : Color.FromArgb(0xE4, 0, 0, 0)));

        // Brand shades used directly by BillKro Rx views.
        overrides["BrandBrush"] = Freeze(new SolidColorBrush(Hex(IsDark ? "#2DD4BF" : "#0F766E")));
        overrides["BrandSoftBrush"] = Freeze(new SolidColorBrush(IsDark ? Color.FromArgb(0x33, 0x2D, 0xD4, 0xBF) : Color.FromArgb(0x1F, 0x0D, 0x94, 0x88)));
        overrides["BrandGradientBrush"] = Freeze(new LinearGradientBrush(Hex("#0F766E"), Hex("#0EA5A4"), 35));
        overrides["BrandHeroBrush"] = Freeze(new LinearGradientBrush(
            [new GradientStop(Hex("#134E4A"), 0), new GradientStop(Hex("#0F766E"), 0.55), new GradientStop(Hex("#14B8A6"), 1)],
            new Point(0, 0), new Point(1, 1)));

        // Airier grids app-wide: taller rows, horizontal rules only. Rebuilt per theme because the base style changes.
        if (fluent[typeof(DataGrid)] is Style grid)
        {
            var airy = new Style(typeof(DataGrid), grid);
            airy.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
            airy.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, new DynamicResourceExtension("DividerStrokeColorDefaultBrush")));
            airy.Setters.Add(new Setter(DataGrid.MinRowHeightProperty, 36.0));

            // Fluent's cell template top-aligns content, which leaves text and chips off-centre in the taller rows.
            // Set through CellStyle: the Fluent grid style assigns its own, which beats an implicit DataGridCell style.
            var baseCell = SetterValue(grid, DataGrid.CellStyleProperty) as Style ?? fluent[typeof(DataGridCell)] as Style;
            var centred = baseCell is null ? new Style(typeof(DataGridCell)) : new Style(typeof(DataGridCell), baseCell);
            centred.Setters.Add(new Setter(Control.TemplateProperty, CentredCellTemplate.Value));
            // Fluent paints selection inside the template replaced above, so restore it here.
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("DataGridRowSelectedBackgroundThemeBrush")));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("DataGridRowSelectedBackgroundThemeBrush")));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("DataGridRowSelectedForegroundThemeBrush")));
            centred.Triggers.Add(selected);
            airy.Setters.Add(new Setter(DataGrid.CellStyleProperty, centred));
            overrides[typeof(DataGrid)] = airy;
        }


        // List rows span the full width so column layouts inside item templates line up.
        if (fluent[typeof(ListBoxItem)] is Style item)
        {
            var stretched = new Style(typeof(ListBoxItem), item);
            stretched.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            overrides[typeof(ListBoxItem)] = stretched;
        }

        merged.Add(overrides);
        _overrides = overrides;
        Changed?.Invoke();
    }

    /// <summary>Value a style (or one it is based on) sets for a property, e.g. the cell style Fluent's grid assigns.</summary>
    private static object? SetterValue(Style? style, DependencyProperty property)
    {
        for (; style is not null; style = style.BasedOn)
            if (style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == property) is { } setter)
                return setter.Value;
        return null;
    }

    /// <summary>
    /// Every resource visible through the dictionary. In System mode Fluent loads a wrapper whose colours sit in
    /// nested dictionaries; later dictionaries win, like WPF's own lookup.
    /// </summary>
    private static Dictionary<object, object> Flatten(ResourceDictionary dictionary)
    {
        var all = new Dictionary<object, object>();
        foreach (var nested in dictionary.MergedDictionaries)
            foreach (var (key, value) in Flatten(nested))
                all[key] = value;
        foreach (var key in dictionary.Keys)
            all[key] = dictionary[key];
        return all;
    }

    private static Brush Swap(GradientBrush source, Dictionary<Color, Color> map)
    {
        var copy = source.Clone();
        foreach (var stop in copy.GradientStops)
            if (map.TryGetValue(stop.Color, out var c)) stop.Color = c;
        return Freeze(copy);
    }

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
