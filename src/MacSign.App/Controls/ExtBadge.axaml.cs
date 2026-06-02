using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MacSign.App.Controls;

/// <summary>The file-type chip: a mono label on a 14%-fill / 28%-border tint,
/// colored per extension (from the prototype's EXT_META).</summary>
public partial class ExtBadge : UserControl
{
    private static readonly Dictionary<string, Color> Palette = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["exe"] = Color.Parse("#5A78D6"),
        ["dll"] = Color.Parse("#3E93A6"),
        ["sys"] = Color.Parse("#7E6CC0"),
        ["msi"] = Color.Parse("#2F9E63"),
        ["ps1"] = Color.Parse("#4A84D6"),
    };

    public static readonly StyledProperty<string> ExtProperty =
        AvaloniaProperty.Register<ExtBadge, string>(nameof(Ext), "");

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ExtBadge, double>(nameof(Size), 30);

    public string Ext { get => GetValue(ExtProperty); set => SetValue(ExtProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public ExtBadge()
    {
        InitializeComponent();
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ExtProperty || change.Property == SizeProperty)
            Apply();
    }

    private void Apply()
    {
        var ext = (Ext ?? "").ToLowerInvariant();
        var c = Palette.TryGetValue(ext, out var v) ? v : Color.Parse("#888888");

        Chip.Width = Chip.Height = Size;
        Chip.CornerRadius = new CornerRadius(Size * 0.23);
        Chip.Background = new SolidColorBrush(c, 0.14);
        Chip.BorderBrush = new SolidColorBrush(c, 0.28);

        Label.Text = ext.ToUpperInvariant();
        Label.Foreground = new SolidColorBrush(c);
        Label.FontSize = Size * 0.3;
    }
}
