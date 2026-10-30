using System.Windows;
using System.Windows.Controls;

namespace MargPos.Desktop.Views;

/// <summary>
/// Friendly placeholder shown over an empty list: a glyph in soft brand circles, a title and a hint.
/// The look lives in the implicit style in Styles.xaml.
/// </summary>
public class EmptyState : Control
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata("Nothing here yet"));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(EmptyState), new PropertyMetadata(null));

    static EmptyState()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(EmptyState), new UIPropertyMetadata(false));
        FocusableProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(false));
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}
