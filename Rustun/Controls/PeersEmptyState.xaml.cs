using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rustun.Controls;

public sealed partial class PeersEmptyState : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(PeersEmptyState),
        new PropertyMetadata(string.Empty, static (d, e) =>
        {
            if (d is PeersEmptyState c)
            {
                c.TitleBlock.Text = e.NewValue as string ?? string.Empty;
            }
        }));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(PeersEmptyState),
        new PropertyMetadata(string.Empty, static (d, e) =>
        {
            if (d is PeersEmptyState c)
            {
                c.DescriptionBlock.Text = e.NewValue as string ?? string.Empty;
            }
        }));

    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph),
        typeof(string),
        typeof(PeersEmptyState),
        new PropertyMetadata("\uE7BA", static (d, e) =>
        {
            if (d is PeersEmptyState c)
            {
                c.StateIcon.Glyph = e.NewValue as string ?? "\uE7BA";
            }
        }));

    public PeersEmptyState()
    {
        InitializeComponent();
        TitleBlock.Text = Title;
        DescriptionBlock.Text = Description;
        StateIcon.Glyph = IconGlyph;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Segoe MDL2 Assets glyph string (e.g. "\uE7BA").</summary>
    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }
}
