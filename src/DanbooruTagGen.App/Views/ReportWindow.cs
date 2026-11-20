using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DanbooruTagGen.App.Views;

/// <summary>긴 텍스트 리포트(레시피 비교 등)를 띄우는 창. 읽기 전용 고정폭 텍스트 + 복사 버튼.</summary>
public sealed class ReportWindow : Window
{
    public ReportWindow(string title, string text)
    {
        Title = title;
        Width = 960;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var copy = new Button { Content = "📋 복사", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { /* 다른 프로그램이 클립보드를 쥐고 있음 — 다시 누르면 된다 */ }
        };
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas, Malgun Gothic"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(copy, Dock.Top);
        dock.Children.Add(copy);
        dock.Children.Add(box);
        Content = dock;
    }
}
