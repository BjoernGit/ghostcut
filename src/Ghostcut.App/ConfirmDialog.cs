using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Ghostcut.App;

/// <summary>Small modal question with up to three buttons; returns the index of the clicked button (-1 = closed).</summary>
public static class ConfirmDialog
{
    public static async Task<int> AskAsync(Window owner, string title, string message, params string[] buttons)
    {
        var result = -1;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 80 };
            button.Click += (_, _) =>
            {
                result = index;
                dialog.Close();
            };
            row.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, row },
        };
        await dialog.ShowDialog(owner);
        return result;
    }
}
