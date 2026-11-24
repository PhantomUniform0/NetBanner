using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace NetBanner.Config;

/// <summary>Minimal yes/no dialog.</summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string yes, string no)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var yesButton = new Button { Content = yes, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var noButton = new Button { Content = no, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        yesButton.Click += (_, _) => Close(true);
        noButton.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yesButton, noButton },
                },
            },
        };
    }

    public static Task<bool> Show(Window owner, string title, string message, string yes = "Yes", string no = "No") =>
        new ConfirmDialog(title, message, yes, no).ShowDialog<bool>(owner);
}
