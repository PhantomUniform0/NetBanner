using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NetBanner.Core;

namespace NetBanner.UI;

/// <summary>
/// Draws one banner: center marking plus left/right indicators. The real banner and the config
/// tool's preview both use it, so the preview matches what users see.
/// </summary>
public sealed class BannerStrip : Border
{
    private readonly TextBlock _center = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeight.Bold,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly StackPanel _left = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private readonly StackPanel _right = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private readonly bool _centerOverflow;

    /// <param name="centerOverflowingText">
    /// Centre text whose line box is taller than the strip (Linux fonts at 13px in a 14px strip), so it overhangs
    /// evenly instead of being cut off at the bottom. Off on Windows, where the approved look is unchanged.
    /// </param>
    public BannerStrip(bool centerOverflowingText = false)
    {
        _centerOverflow = centerOverflowingText;
        var panel = new Panel();
        panel.Children.Add(Wrap(_center));
        panel.Children.Add(_left);
        panel.Children.Add(_right);
        Child = panel;
        ClipToBounds = true;
    }

    private Control Wrap(TextBlock text) =>
        _centerOverflow
            ? new OverflowCenter { Child = text, HorizontalAlignment = text.HorizontalAlignment }
            : text;

    /// <summary>Renders <paramref name="settings"/>. The caller sets the strip's height.</summary>
    public void Render(BannerSettings settings, Func<string, string?>? tokens)
    {
        var view = BannerPresenter.Present(settings, tokens);
        var fg = Brush(view.Foreground, Colors.Black);

        Background = Brush(view.Background, Colors.White);
        _center.Text = view.CenterText;
        _center.Foreground = fg;
        _center.FontSize = settings.FontSize;

        Fill(_left, view.Left, fg, settings.FontSize);
        Fill(_right, view.Right, fg, settings.FontSize);
    }

    private void Fill(StackPanel panel, IReadOnlyList<SideItemView> items, IBrush defaultFg, double fontSize)
    {
        panel.Children.Clear();
        foreach (var item in items)
        {
            panel.Children.Add(new Border
            {
                Padding = new Thickness(10, 0),
                Background = item.Background is null ? null : Brush(item.Background, Colors.Transparent),
                Child = Wrap(new TextBlock
                {
                    Text = item.Text,
                    FontSize = fontSize,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = item.Foreground is null ? defaultFg : Brush(item.Foreground, Colors.Black),
                }),
            });
        }
    }

    public static IBrush Brush(string hex, Color fallback) =>
        new SolidColorBrush(Color.TryParse(hex, out var c) ? c : fallback);

    /// <summary>
    /// Lets its child be taller than the space it's given and centres it vertically, so the overflow is shared
    /// between top and bottom (the strip clips both) instead of all of it being cut off at the bottom.
    /// </summary>
    private sealed class OverflowCenter : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child is null)
            {
                return default;
            }

            Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return new Size(Child.DesiredSize.Width, Math.Min(Child.DesiredSize.Height, availableSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child is not null)
            {
                double h = Child.DesiredSize.Height;
                Child.Arrange(new Rect(0, Math.Floor((finalSize.Height - h) / 2), finalSize.Width, h));
            }

            return finalSize;
        }
    }
}
