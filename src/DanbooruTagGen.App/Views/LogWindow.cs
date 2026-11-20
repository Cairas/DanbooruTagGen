using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Logging;

namespace DanbooruTagGen.App.Views;

/// <summary>문제 로그 창(도구 ▾ → 로그…): 날짜별, [전체/컨셉 문제/프로그램 문제] 필터, 이름·글 검색, 복사, 폴더 열기.
/// 로그는 문제만 담는다(프로그램 문제·컨셉 제작 문제 파악용, 7일 보관).</summary>
public sealed class LogWindow : Window
{
    private readonly ComboBox _days = new() { Width = 130, Margin = new Thickness(6, 0, 0, 0) };
    private readonly RadioButton _all = new() { Content = "전체", IsChecked = true, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _concept = new() { Content = "컨셉 문제", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _program = new() { Content = "프로그램 문제", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { Width = 220, Margin = new Thickness(6, 0, 0, 0), ToolTip = "레시피 이름·제목·내용에서 찾기" };
    private readonly ListBox _list = new() { Margin = new Thickness(0, 6, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _info = new() { FontSize = 11, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4) };
    private IReadOnlyList<LogEntry> _entries = Array.Empty<LogEntry>();

    public LogWindow()
    {
        Title = "로그 — 문제만, 최근 7일";
        Width = 1000;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);

        var copy = new Button { Content = "선택 항목 복사", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        var copyAll = new Button { Content = "보이는 것 전부 복사", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        var refresh = new Button { Content = "새로고침", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        var open = new Button { Content = "로그 폴더 열기", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        copy.Click += (_, _) => Copy(_list.SelectedItems.Cast<ListBoxItem>().Select(i => (LogEntry)i.Tag).ToList());
        copyAll.Click += (_, _) => Copy(_list.Items.Cast<ListBoxItem>().Select(i => (LogEntry)i.Tag).ToList());
        refresh.Click += (_, _) => LoadDays();
        open.Click += (_, _) =>
        {
            System.IO.Directory.CreateDirectory(AppLog.Log.Directory);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLog.Log.Directory) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { _info.Text = "폴더를 열지 못했습니다: " + ex.Message; }
        };
        _list.SelectionMode = SelectionMode.Extended;
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                Copy(_list.SelectedItems.Cast<ListBoxItem>().Select(i => (LogEntry)i.Tag).ToList());
        };
        foreach (var rb in new[] { _all, _concept, _program }) rb.Checked += (_, _) => Render();
        _search.TextChanged += (_, _) => Render();
        _days.SelectionChanged += (_, _) => LoadEntries();

        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_all);
        bar.Children.Add(_concept);
        bar.Children.Add(_program);
        bar.Children.Add(new TextBlock { Text = "🔍", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        bar.Children.Add(_search);
        bar.Children.Add(new TextBlock { Text = "날짜", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        bar.Children.Add(_days);
        bar.Children.Add(copy);
        bar.Children.Add(copyAll);
        bar.Children.Add(refresh);
        bar.Children.Add(open);

        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_info, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(_info);
        dock.Children.Add(_list);
        Content = dock;
        LoadDays();
    }

    private void LoadDays()
    {
        var keep = _days.SelectedItem as string;
        _days.Items.Clear();
        foreach (var d in AppLog.Log.Days()) _days.Items.Add(d.ToString("yyyy-MM-dd"));
        if (_days.Items.Count == 0) { _entries = Array.Empty<LogEntry>(); Render(); return; }
        _days.SelectedItem = keep != null && _days.Items.Contains(keep) ? keep : _days.Items[0];
        LoadEntries();
    }

    private void LoadEntries()
    {
        _entries = _days.SelectedItem is string s && DateTime.TryParse(s, out var day)
            ? AppLog.Log.Read(day).Reverse().ToList()   // 최신이 위
            : Array.Empty<LogEntry>();
        Render();
    }

    private void Render()
    {
        var q = _search.Text.Trim();
        var shown = _entries
            .Where(e => _all.IsChecked == true || (_concept.IsChecked == true ? e.Kind == LogKind.Concept : e.Kind == LogKind.Program))
            .Where(e => q.Length == 0 || e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || (e.Recipe?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                        || e.Body.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _list.Items.Clear();
        foreach (var e in shown) _list.Items.Add(new ListBoxItem { Tag = e, Content = Card(e) });
        int concept = _entries.Count(e => e.Kind == LogKind.Concept);
        _info.Text = _entries.Count == 0
            ? "이 날짜에는 기록된 문제가 없습니다. (로그는 문제만 남깁니다 — 평범한 생성은 기록하지 않음)"
            : $"{_entries.Count}건(컨셉 {concept} · 프로그램 {_entries.Count - concept}) 중 {shown.Count}건 표시 · 파일: {AppLog.Log.PathOf(DateTime.Parse((string)_days.SelectedItem!))}";
    }

    private static UIElement Card(LogEntry e)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        var head = new TextBlock { TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new System.Windows.Documents.Run($"{e.At:HH:mm:ss}  ") { Foreground = Brushes.Gray });
        head.Inlines.Add(new System.Windows.Documents.Run(e.Kind == LogKind.Concept ? "[컨셉] " : "[프로그램] ")
            { Foreground = e.Kind == LogKind.Concept ? Brushes.DarkOrange : Brushes.Firebrick, FontWeight = FontWeights.Bold });
        head.Inlines.Add(new System.Windows.Documents.Run(e.Title) { FontWeight = FontWeights.Bold });
        if (e.Recipe is { } r) head.Inlines.Add(new System.Windows.Documents.Run("  ·  " + r) { Foreground = Brushes.DarkSlateGray });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock
        {
            Text = e.Body,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Malgun Gothic"),
            FontSize = 11.5,
            Margin = new Thickness(14, 2, 0, 0),
        });
        return panel;
    }

    private void Copy(IReadOnlyList<LogEntry> entries)
    {
        if (entries.Count == 0) { _info.Text = "복사할 항목을 고르세요(Ctrl/Shift로 여러 개)."; return; }
        var text = string.Join(Environment.NewLine + Environment.NewLine, entries.Select(e =>
            $"[{e.At:yyyy-MM-dd HH:mm:ss}] [{(e.Kind == LogKind.Concept ? "컨셉" : "프로그램")}] {e.Title}"
            + (e.Recipe is { } r ? Environment.NewLine + "레시피: " + r : "")
            + Environment.NewLine + e.Body));
        try { Clipboard.SetText(text); _info.Text = $"{entries.Count}건을 클립보드에 복사했습니다."; }
        catch (System.Runtime.InteropServices.ExternalException) { _info.Text = "클립보드를 다른 프로그램이 쓰고 있어 복사하지 못했습니다."; }
    }
}
