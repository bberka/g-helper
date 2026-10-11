using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GHelper.App.Controls;

namespace GHelper.App.Services;

/// <summary>
/// Themed replacement for MessageBox (Yes/No confirmation or OK alert). <see cref="Run"/> blocks the calling code
/// like the legacy MessageBox.Show did: it spins a nested dispatcher frame until the dialog is closed.
/// </summary>
public sealed class MessageDialog : Window
{
    private readonly bool _confirm;

    public bool Result { get; private set; }

    public MessageDialog(string title, string text, bool confirm)
    {
        _confirm = confirm;

        Title = string.IsNullOrEmpty(title) ? "G-Helper" : title;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 460;
        MinWidth = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };

        if (confirm)
        {
            var yes = new RButton { Content = "Yes", Width = 84, Height = 28 };
            yes.Click += (_, _) => Finish(true);
            var no = new RButton { Content = "No", Width = 84, Height = 28 };
            no.Click += (_, _) => Finish(false);
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = new RButton { Content = "OK", Width = 84, Height = 28 };
            ok.Click += (_, _) => Finish(true);
            buttons.Children.Add(ok);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { message, buttons },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(!confirm ? true : false);
            else if (e.Key == Key.Enter) Finish(true);
        };
    }

    private void Finish(bool result)
    {
        Result = result;
        Close();
    }

    /// <summary>Show the dialog and wait for it (UI thread only).</summary>
    public static bool Run(Window? owner, string title, string text, bool confirm)
    {
        var dialog = new MessageDialog(title, text, confirm);
        var frame = new DispatcherFrame();
        dialog.Closed += (_, _) => frame.Continue = false;

        if (owner is { IsVisible: true })
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();

        Dispatcher.UIThread.PushFrame(frame);
        return dialog.Result;
    }
}
