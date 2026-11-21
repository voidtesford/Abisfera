using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Abisfera.AnimEditor.UI;

public static class Dialogs
{
    /// <summary>Modal message with buttons; returns the index of the pressed button (-1 when closed).</summary>
    public static async Task<int> Ask(Window owner, string title, string text, params string[] buttons)
    {
        var result = -1;
        var win = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (var i = 0; i < buttons.Length; i++)
        {
            var idx = i;
            var b = new Button { Content = buttons[i], IsDefault = i == 0, IsCancel = i == buttons.Length - 1 && buttons.Length > 1 };
            b.Click += (_, _) =>
            {
                result = idx;
                win.Close();
            };
            row.Children.Add(b);
        }

        win.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                },
                row,
            },
        };
        await win.ShowDialog(owner);
        return result;
    }

    public static Task Info(Window owner, string title, string text) => Ask(owner, title, text, "OK");

    /// <summary>Asks for a few text values; null when cancelled.</summary>
    public static async Task<string[]?> Prompt(Window owner, string title, string? note, params (string Label, string Value)[] fields)
    {
        string[]? result = null;
        var boxes = fields.Select(f => new TextBox { Text = f.Value, MinWidth = 380 }).ToArray();
        var win = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        if (note != null)
            panel.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        for (var i = 0; i < fields.Length; i++)
        {
            panel.Children.Add(new TextBlock { Text = fields[i].Label, Margin = new Thickness(0, 6, 0, 0) });
            panel.Children.Add(boxes[i]);
        }

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = boxes.Select(b => b.Text ?? "").ToArray();
            win.Close();
        };
        cancel.Click += (_, _) => win.Close();
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
        win.Content = panel;
        win.Opened += (_, _) => boxes.FirstOrDefault()?.Focus();
        await win.ShowDialog(owner);
        return result;
    }
}
