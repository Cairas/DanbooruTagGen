using System.Windows;
using System.Windows.Controls;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.App.ViewModels;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.Views;

/// <summary>백업 목록과 복원. 목록은 최신순, 고른 백업에 든 파일을 아래에 보여 준다.</summary>
public sealed class BackupWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ListBox _list = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = System.Windows.Media.Brushes.DimGray };

    private sealed record Row(BackupEntry Entry)
    {
        public override string ToString() =>
            $"{Entry.Time:yyyy-MM-dd HH:mm}   {(Entry.IsDaily ? "📅" : "📌")} {Entry.Label}   ({Entry.Bytes / 1024.0:N0} KB)";
    }

    public BackupWindow(MainViewModel main)
    {
        _main = main;
        Title = "백업";
        Width = 620;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var info = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Text = $"📅 자동: 그날 첫 실행 때(시딩 전 상태, 최근 {BackupStore.KeepDaily}일)   📌 스냅샷: 프리셋 갱신·제거·복원 직전(최근 {BackupStore.KeepSnapshots}개)\n"
                 + "레시피·풀·훅·일괄 생성 프리셋·설정(시딩 장부 포함)을 한 묶음으로 되돌립니다.",
        };
        var restore = new Button { Content = "이 시점으로 복원…", Padding = new Thickness(10, 2, 10, 2), FontWeight = FontWeights.Bold };
        var now = new Button { Content = "지금 백업", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        var open = new Button { Content = "폴더 열기", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        restore.Click += (_, _) =>
        {
            if (_list.SelectedItem is Row row) _main.RestoreBackup(row.Entry);
            else _main.Status = "되돌릴 백업을 고르세요.";
        };
        now.Click += (_, _) =>
        {
            if (_main.Snapshot("수동")) { _main.Status = "지금 상태를 백업했습니다."; Reload(); }
        };
        open.Click += (_, _) =>
        {
            System.IO.Directory.CreateDirectory(AppPaths.BackupsDir);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.BackupsDir) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { _main.Status = "폴더를 열지 못했습니다: " + ex.Message; }
        };
        _list.SelectionChanged += (_, _) => ShowDetail();
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is Row row) _main.RestoreBackup(row.Entry); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(restore);
        buttons.Children.Add(now);
        buttons.Children.Add(open);

        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(info, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_detail, Dock.Bottom);
        dock.Children.Add(info);
        dock.Children.Add(buttons);
        dock.Children.Add(_detail);
        dock.Children.Add(_list);
        Content = dock;
        Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var e in BackupStore.List(AppPaths.BackupsDir)) _list.Items.Add(new Row(e));
        if (_list.Items.Count == 0) _detail.Text = "아직 백업이 없습니다. '지금 백업'으로 만들 수 있습니다.";
        else _list.SelectedIndex = 0;
    }

    private void ShowDetail()
    {
        if (_list.SelectedItem is not Row row) { _detail.Text = ""; return; }
        try { _detail.Text = "들어 있는 파일: " + string.Join(", ", BackupStore.Contents(row.Entry.Path)); }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException) { _detail.Text = "⚠ 이 백업을 읽지 못했습니다: " + ex.Message; }
    }
}
