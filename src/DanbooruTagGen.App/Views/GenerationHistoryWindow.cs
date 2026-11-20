using System.Windows;
using System.Windows.Controls;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.App.ViewModels;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.Views;

/// <summary>생성 기록 목록: 결과 목록에 다시 띄우기 · 같은 시드로 맞추기. 기록은 "생성"(파일 쓰기)마다 저절로 남는다.</summary>
public sealed class GenerationHistoryWindow : Window
{
    private readonly GenerationViewModel _generation;
    private readonly ListBox _list = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = System.Windows.Media.Brushes.DimGray };

    private sealed record Row(GenerationRecordEntry Entry)
    {
        public override string ToString() => $"{Entry.At:yyyy-MM-dd HH:mm:ss}   {Entry.Summary}";
    }

    public GenerationHistoryWindow(GenerationViewModel generation)
    {
        _generation = generation;
        Title = "생성 기록";
        Width = 600;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var info = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Text = $"'생성'(파일 쓰기)마다 저절로 남습니다(최근 {GenerationHistoryStore.Keep}개). 다시 띄우면 결과 목록에서 잠금·다시 뽑기를 이어서 할 수 있습니다.",
        };
        var load = new Button { Content = "목록에 다시 띄우기", Padding = new Thickness(10, 2, 10, 2), FontWeight = FontWeights.Bold };
        var seed = new Button { Content = "이 시드로 맞추기", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        load.Click += (_, _) => { if (Selected() is { } r) _generation.LoadGenerationRecord(r); };
        seed.Click += (_, _) => { if (Selected() is { } r) _generation.UseSeed(r.Seed); };
        _list.MouseDoubleClick += (_, _) => { if (Selected() is { } r) _generation.LoadGenerationRecord(r); };
        _list.SelectionChanged += (_, _) => ShowDetail();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(load);
        buttons.Children.Add(seed);
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(info, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_detail, Dock.Bottom);
        dock.Children.Add(info);
        dock.Children.Add(buttons);
        dock.Children.Add(_detail);
        dock.Children.Add(_list);
        Content = dock;

        foreach (var e in GenerationHistoryStore.List(AppPaths.GenerationHistoryDir)) _list.Items.Add(new Row(e));
        if (_list.Items.Count == 0) _detail.Text = "아직 기록이 없습니다.";
        else _list.SelectedIndex = 0;
    }

    private GenerationRecord? Selected()
    {
        if (_list.SelectedItem is not Row row) return null;
        var record = GenerationHistoryStore.Load(row.Entry.Path);
        if (record is null) _detail.Text = "⚠ 이 기록을 읽지 못했습니다.";
        return record;
    }

    private void ShowDetail()
    {
        if (Selected() is not { } r) return;
        var recipes = string.Join(", ", r.Recipes.Take(8).Select(x => $"{x.Name}({x.Lines.Count}/{x.Requested})"))
                      + (r.Recipes.Count > 8 ? $" 외 {r.Recipes.Count - 8}개" : "");
        _detail.Text = $"시드 {r.Seed} · {r.Mode} → {r.OutputPath}\n{r.OptionsSummary}\n{recipes}";
    }
}
