using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Cluster.Model;

namespace Cluster.Ui;

/// <summary>Small caps-style label with CSS-like letter-spacing, which TextBlock can't do.</summary>
internal sealed class SpacedText : FrameworkElement
{
    readonly string text;
    readonly double fontSize, spacing;
    readonly Typeface typeface;
    Brush brush;
    FormattedText[]? glyphs;

    public SpacedText(string text, double fontSize, double spacingEm, FontWeight weight, Brush brush)
    {
        this.text = text;
        this.fontSize = fontSize;
        spacing = spacingEm * fontSize;
        this.brush = brush;
        typeface = new Typeface(Theme.TextFont, FontStyles.Normal, weight, FontStretches.Normal);
        Height = Math.Round(fontSize * 1.15);
    }

    public Brush Foreground
    {
        get => brush;
        set
        {
            if (ReferenceEquals(brush, value)) return;
            brush = value;
            glyphs = null;
            InvalidateVisual();
        }
    }

    FormattedText[] Glyphs()
    {
        if (glyphs is not null) return glyphs;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        glyphs = new FormattedText[text.Length];
        for (int i = 0; i < text.Length; i++)
            glyphs[i] = new FormattedText(text[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, fontSize, brush, pixelsPerDip);
        return glyphs;
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = 0;
        var g = Glyphs();
        for (int i = 0; i < g.Length; i++)
            width += g[i].WidthIncludingTrailingWhitespace + (i < g.Length - 1 ? spacing : 0);
        return new Size(Math.Ceiling(width), Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double x = 0;
        foreach (var g in Glyphs())
        {
            dc.DrawText(g, new Point(x, (ActualHeight - g.Height) / 2));
            x += g.WidthIncludingTrailingWhitespace + spacing;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        glyphs = null;
        InvalidateMeasure();
        InvalidateVisual();
    }
}

/// <summary>Dial + stacked label / value / sub-reading, as in Option A.</summary>
internal sealed class GaugeTile : StackPanel
{
    readonly DialElement dial;
    readonly Run value = new(), unit = new();
    readonly TextBlock valueBlock, sub;
    Level level = Level.Normal;

    public GaugeTile(GaugeSpec spec)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        dial = new DialElement(spec) { VerticalAlignment = VerticalAlignment.Top };
        Children.Add(dial);

        unit.FontSize = 17 * 0.6;
        unit.FontWeight = FontWeights.Medium;
        unit.Foreground = Theme.Muted;
        valueBlock = new TextBlock
        {
            FontFamily = Theme.DisplayFont,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Ink,
            LineHeight = 19,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        Typography.SetNumeralAlignment(valueBlock, FontNumeralAlignment.Tabular);
        valueBlock.Inlines.Add(value);
        valueBlock.Inlines.Add(unit);

        sub = new TextBlock
        {
            FontFamily = Theme.TextFont,
            FontSize = 9.5,
            Foreground = Theme.Muted,
            LineHeight = 11,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);

        var text = new StackPanel
        {
            Margin = new Thickness(7, 0, 0, 0),
            MinWidth = 68,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Children.Add(new SpacedText(spec.Label, 9.5, 0.14, FontWeights.SemiBold, Theme.Muted));
        text.Children.Add(valueBlock);
        text.Children.Add(sub);
        Children.Add(text);
        Show(new Readout(Readouts.Dash, "", "", Level.Normal, 0, spec.Label));
    }

    public void Show(in Readout r)
    {
        value.Text = r.Value;
        unit.Text = r.Unit.Length > 0 ? " " + r.Unit : "";
        sub.Text = r.Sub;
        if (r.Level != level)
        {
            level = r.Level;
            valueBlock.Foreground = Theme.ForLevel(level);
        }
        if (!Equals(ToolTip, r.Tooltip)) ToolTip = r.Tooltip;
        dial.SetTarget(r.Position);
    }

    public void Detach() => NeedleAnimator.Forget(dial);
}

/// <summary>The four gauges, laid out horizontally (top/bottom/overlay/taskbar) or vertically (left/right).</summary>
internal sealed class ClusterView : StackPanel
{
    readonly GaugeTile cpu = new(GaugeSpec.Cpu), ram = new(GaugeSpec.Ram), disk = new(GaugeSpec.Disk), net = new(GaugeSpec.Net);
    readonly GaugeTile[] tiles;

    /// <param name="vertical">Column for left/right docking.</param>
    /// <param name="compact">Taskbar layout: tighter gaps.</param>
    public ClusterView(bool vertical, bool compact = false)
    {
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        double gap = compact ? 16 : 28;
        tiles = [cpu, ram, disk, net];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = i == 0 ? new Thickness(0) : vertical ? new Thickness(0, 10, 0, 0) : new Thickness(gap, 0, 0, 0);
            Children.Add(tiles[i]);
        }
    }

    /// <summary>Shows the first <paramref name="tileCount"/> gauges (CPU, RAM, DISK, NET order).</summary>
    public void SetParts(int tileCount)
    {
        for (int i = 0; i < tiles.Length; i++)
            tiles[i].Visibility = i < tileCount ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Width in DIPs this view would need with <paramref name="tileCount"/> gauges.</summary>
    public double MeasureWidth(int tileCount)
    {
        SetParts(tileCount);
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return DesiredSize.Width;
    }

    public void Show(Snapshot s)
    {
        var r = s.Readings;
        cpu.Show(Readouts.Cpu(r));
        ram.Show(Readouts.Ram(r));
        disk.Show(Readouts.Disk(r));
        net.Show(Readouts.Net(r));
    }

    public void Detach()
    {
        cpu.Detach(); ram.Detach(); disk.Detach(); net.Detach();
    }
}
