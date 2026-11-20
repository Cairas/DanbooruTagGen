using System.Windows;
using System.Windows.Controls;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.App.ViewModels;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.Views;

/// <summary>레시피 하나의 버전 목록: 지금과 비교 · 이 버전으로 되돌리기 · 새 레시피로 복제.</summary>
public sealed class RecipeVersionsWindow : Window
{
    private readonly RecipeLibraryViewModel _library;
    private readonly Recipe _recipe;
    private readonly ListBox _list = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = System.Windows.Media.Brushes.DimGray };

    private sealed record Row(RecipeVersion Version)
    {
        public override string ToString() => $"{Version.Time:yyyy-MM-dd HH:mm:ss}   {Version.Reason}";
    }

    public RecipeVersionsWindow(RecipeLibraryViewModel library, Recipe recipe)
    {
        _library = library;
        _recipe = recipe;
        Title = $"버전 — {recipe.Name}";
        Width = 560;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var info = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Text = $"열 때(기준)·고칠 때(편집, {RecipeHistoryStore.CoalesceWindow.TotalMinutes:0}분 안의 연속 편집은 하나로)·프리셋 갱신 전후에 저절로 남습니다. "
                 + $"레시피마다 최근 {RecipeHistoryStore.KeepPerRecipe}개.",
        };
        var compare = new Button { Content = "⚖ 지금과 비교", Padding = new Thickness(10, 2, 10, 2) };
        var restore = new Button { Content = "이 버전으로 되돌리기…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        var clone = new Button { Content = "새 레시피로 복제", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        compare.Click += async (_, _) =>
        {
            if (Selected() is not { } v) return;
            await _library.ShowComparisonAsync(v.Recipe, $"{v.Row.Version.Time:MM-dd HH:mm} 버전 ({v.Row.Version.Reason})", _recipe, "지금");
        };
        restore.Click += (_, _) =>
        {
            if (Selected() is not { } v) return;
            var ok = MessageBox.Show($"'{_recipe.Name}'을(를) {v.Row.Version.Time:yyyy-MM-dd HH:mm} 버전으로 되돌릴까요?\n지금 내용은 '되돌리기 전' 버전으로 남습니다.",
                "버전 되돌리기", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
            _library.RestoreVersion(_recipe, v.Recipe, v.Row.Version.Time);
            Reload();
        };
        clone.Click += (_, _) =>
        {
            if (Selected() is not { } v) return;
            _library.CloneVersionAsNew(_recipe, v.Recipe, v.Row.Version.Time);
        };
        _list.SelectionChanged += (_, _) => ShowDetail();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(compare);
        buttons.Children.Add(restore);
        buttons.Children.Add(clone);

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

    private (Row Row, Recipe Recipe)? Selected()
    {
        if (_list.SelectedItem is not Row row) return null;
        var recipe = RecipeHistoryStore.Load(row.Version);
        if (recipe is null) { _detail.Text = "⚠ 이 버전 파일을 읽지 못했습니다."; return null; }
        return (row, recipe);
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var v in RecipeHistoryStore.List(AppPaths.HistoryDir, _recipe.Id)) _list.Items.Add(new Row(v));
        if (_list.Items.Count == 0) _detail.Text = "아직 버전이 없습니다. 레시피를 열거나 고치면 저절로 남습니다.";
        else _list.SelectedIndex = 0;
    }

    private void ShowDetail()
    {
        if (Selected() is not { } v) { if (_list.SelectedItem is null) _detail.Text = ""; return; }
        var r = v.Recipe;
        _detail.Text = $"이름: {r.Name}\n슬롯 {r.Slots.Count}개: " + string.Join(" · ", r.Slots.Select(s => string.IsNullOrWhiteSpace(s.Label) ? "(이름 없음)" : s.Label));
    }
}
