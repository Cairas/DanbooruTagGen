using System.Windows;
using System.Windows.Controls;

namespace DanbooruTagGen.App.Views;

/// <summary>이름 하나를 묻는 작은 창(선택 프리셋 "새로 저장…" 등). 취소하거나 빈 이름이면 null.</summary>
public sealed class NamePromptWindow : Window
{
    private readonly TextBox _box = new() { Margin = new Thickness(0, 6, 0, 8) };

    private NamePromptWindow(string title, string prompt, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _box.Text = initial;

        var ok = new Button { Content = "확인", IsDefault = true, Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "취소", IsCancel = true, Padding = new Thickness(14, 2, 14, 2) };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(string title, string prompt, string initial = "")
    {
        var win = new NamePromptWindow(title, prompt, initial) { Owner = Application.Current.MainWindow };
        return win.ShowDialog() == true && win._box.Text.Trim() is { Length: > 0 } name ? name : null;
    }
}
