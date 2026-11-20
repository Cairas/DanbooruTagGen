using System.Windows;
using System.Windows.Controls;
using DanbooruTagGen.App.ViewModels;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.Views;

/// <summary>지운 제공 팩 목록 — 고른 것을 다시 넣는다. 한 번 시딩된 팩은 지우면 장부가 기억해 다시 저절로 들어오지 않으므로,
/// 되살릴 길이 여기뿐이다(같은 id를 새 팩에 다시 쓰면 앱에 영영 안 나타나는 것도 같은 이유).</summary>
public sealed class DeletedPresetsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };

    private sealed record Row(Recipe Recipe)
    {
        public override string ToString() => $"{Recipe.Name}   ({Recipe.Id})";
    }

    public DeletedPresetsWindow(MainViewModel main)
    {
        _main = main;
        Title = "지운 제공 팩";
        Width = 640;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var restore = new Button { Content = "선택한 팩 다시 넣기", Padding = new Thickness(10, 2, 10, 2), FontWeight = FontWeights.Bold };
        var all = new Button { Content = "전체 선택", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        restore.Click += (_, _) =>
        {
            var picked = _list.SelectedItems.Cast<Row>().Select(r => r.Recipe).ToList();
            if (picked.Count == 0) { _info.Text = "다시 넣을 팩을 고르세요(Ctrl/Shift로 여러 개)."; return; }
            _main.RestoreBundledRecipes(picked);
            Reload();
        };
        all.Click += (_, _) => _list.SelectAll();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(restore);
        buttons.Children.Add(all);
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(_info, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(_info);
        dock.Children.Add(buttons);
        dock.Children.Add(_list);
        Content = dock;
        Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        var deleted = _main.DeletedBundledRecipes(out var error);
        if (deleted is null) { _info.Text = "⚠ 번들 프리셋을 읽지 못했습니다: " + error; return; }
        foreach (var r in deleted) _list.Items.Add(new Row(r));
        _info.Text = deleted.Count == 0
            ? "지운 제공 팩이 없습니다."
            : $"한 번 들어왔다가 지운 제공 팩 {deleted.Count}개입니다(번들에는 아직 있음). 다시 넣으면 목록 맨 뒤에 🆕로 들어갑니다.";
    }
}
