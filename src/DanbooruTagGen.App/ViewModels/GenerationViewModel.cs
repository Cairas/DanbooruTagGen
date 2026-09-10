using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DanbooruTagGen.App.Services;
using DanbooruTagGen.Core.Generation;
using DanbooruTagGen.Core.Models;
using DanbooruTagGen.Core.Output;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.ViewModels;

/// <summary>여러 레시피 일괄 생성 모드에서 체크할 수 있는 레시피 한 항목.</summary>
public sealed partial class SelectableRecipeItem : ObservableObject
{
    public SelectableRecipeItem(Recipe recipe)
    {
        Recipe = recipe;
        SearchBlob = BuildSearchBlob(recipe);
    }
    public Recipe Recipe { get; }
    public string Name => Recipe.Name;
    /// <summary>NSFW 레시피는 이름이 🔞로 시작한다(전 팩이 지키는 규칙). 카테고리 문자열은
    /// "감금·강제·논콘"/"최대강도"처럼 제각각이라 판정 기준으로 쓰기 어렵다.</summary>
    public bool IsNsfw => Recipe.Name.StartsWith("🔞", StringComparison.Ordinal);
    /// <summary>"프리뷰" 라벨이 붙은 포즈 레퍼런스 샷(전신 누드 스탠딩 등)인지. "🔞 전체"/
    /// "SFW 전체" 일괄 선택 버튼 둘 다에서 제외하는 용도 — 매번 수동으로 체크 해제해야
    /// 하는 게 번거롭다는 피드백으로 추가했다. SFW로 잘못 편입되면 안 되므로 IsNsfw 자체를
    /// 바꾸지 않고 별도 플래그로 뺐다.</summary>
    public bool IsPreview => Recipe.Labels.Contains("프리뷰");
    [ObservableProperty] private bool _isChecked;

    /// <summary>이름 + Labels + 모든 슬롯/그룹 라벨 + 모든 danbooru 태그를 한 문자열로 모은
    /// 검색용 블롭. "netorare"/"네토라레"를 검색창에 치면 사용자가 따로 라벨을 안 붙였어도
    /// 레시피에 실제로 들어있는 태그나 축 이름(둘 다 컨셉 작업 중 항상 채워짐)으로 찾힌다 —
    /// Labels는 태그·축 이름만으론 못 잡는 서사적 분류(예: "타락")를 보완하는 용도로만 쓴다.</summary>
    public string SearchBlob { get; }

    private static string BuildSearchBlob(Recipe recipe)
    {
        var parts = new List<string> { recipe.Name };
        parts.AddRange(recipe.Labels);
        foreach (var slot in recipe.Slots)
        {
            parts.Add(slot.Label);
            switch (slot)
            {
                case FixedSlot f: parts.AddRange(f.Tags); break;
                case RandomPoolSlot p: parts.AddRange(p.Tags); break;
                case AlternativeSlot a:
                    foreach (var g in a.Groups) { parts.Add(g.Label); parts.AddRange(g.Tags); }
                    break;
            }
        }
        return string.Join('␟', parts);
    }
}

public sealed partial class GenerationViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly WildcardGenerator _generator = new();

    /// <summary>단일(현재 빌더) vs 여러 레시피 일괄 생성. 기본 단일 — 기존 동작 그대로 보존.</summary>
    [ObservableProperty] private bool _isMultiRecipeMode;
    /// <summary>일괄 생성 체크 목록(검색 필터를 거친 표시용). 마스터 목록(_allBatchItems)에서
    /// 다시 골라 채우므로, 검색으로 가렸다 다시 보여도 체크 상태가 유지된다.</summary>
    public ObservableCollection<SelectableRecipeItem> BatchItems { get; } = new();
    private readonly List<SelectableRecipeItem> _allBatchItems = new();
    /// <summary>이름뿐 아니라 태그·축 라벨·Labels까지 훑는다(SelectableRecipeItem.SearchBlob) —
    /// "netorare"/"네토라레"처럼 컨셉과 관련된 말을 치면 그 태그나 축 이름을 가진 레시피가 걸린다.</summary>
    [ObservableProperty] private string _batchSearchText = "";

    [ObservableProperty] private int _lineCount = 100;
    /// <summary>"-1"은 "매번 랜덤"을 뜻하는 값(빈 문자열도 같은 뜻으로 계속 받아들임).
    /// 빈 칸으로 두면 "설정을 깜빡한 건가 랜덤인 건가" 헷갈릴 수 있어, 기본값 자체를
    /// -1로 명시해 화면에 보이게 했다.</summary>
    [ObservableProperty] private string _seedText = "-1";
    [ObservableProperty] private bool _dedupeWithinLine = true;
    [ObservableProperty] private bool _avoidDuplicateLines = true;
    [ObservableProperty] private bool _avoidConflicts = true;
    [ObservableProperty] private bool _insertBlankLine = true;
    [ObservableProperty] private bool _underscoreToSpace = true;
    /// <summary>빈도 가중 추첨(인기 태그가 더 자주). 기본 꺼짐 — 균등 추첨.</summary>
    [ObservableProperty] private bool _weightedSampling;
    /// <summary>표준 프롬프트 순서로 자동 정렬. 기본 꺼짐.</summary>
    [ObservableProperty] private bool _autoOrderTags;
    /// <summary>켜면 QualityTagsText를 매 줄 맨 앞에 붙인다. 자기만의 품질 태그 워크플로가
    /// 없는 사용자도 이 프로그램만으로 완성된 프롬프트를 바로 뽑을 수 있게 하는 옵트인 기능.</summary>
    [ObservableProperty] private bool _qualityTagsEnabled;
    [ObservableProperty] private string _qualityTagsText = "";
    [ObservableProperty] private WriteMode _mode = WriteMode.Append;
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _conflictReport = "";
    /// <summary>백그라운드 생성 진행률("1,200/22,700줄 (5%)"). 비어 있으면 진행 중이 아니다.</summary>
    [ObservableProperty] private string _progressText = "";

    public WriteMode[] Modes { get; } = { WriteMode.New, WriteMode.Overwrite, WriteMode.Append };

    public GenerationViewModel(MainViewModel main)
    {
        _main = main;
        LoadSettings();
        foreach (var r in _main.SavedRecipes) _allBatchItems.Add(new SelectableRecipeItem(r));
        RefreshBatchItems();
    }

    partial void OnBatchSearchTextChanged(string value) => RefreshBatchItems();

    /// <summary>레시피 라이브러리가 바깥에서 바뀐 뒤(예: 번들 프리셋 갱신, 라벨 편집) 체크
    /// 목록을 다시 채운다. 이미 체크해 둔 레시피는 Id로 찾아 체크 상태를 그대로 옮긴다.</summary>
    public void RefreshBatchRecipes()
    {
        var checkedIds = _allBatchItems.Where(b => b.IsChecked).Select(b => b.Recipe.Id).ToHashSet();
        _allBatchItems.Clear();
        foreach (var r in _main.SavedRecipes)
        {
            var item = new SelectableRecipeItem(r);
            if (checkedIds.Contains(r.Id)) item.IsChecked = true;
            _allBatchItems.Add(item);
        }
        RefreshBatchItems();
    }

    /// <summary>레시피 라이브러리 창에서 여러 레시피를 골라 "일괄 생성으로 보내기"를 눌렀을 때
    /// 호출된다. 기존 체크 상태는 이 선택으로 완전히 교체하고(라이브러리에서 방금 고른 것과
    /// 옛 체크가 섞여 나오는 걸 막음), 일괄 생성 모드도 함께 켠다.</summary>
    public void SetBatchSelection(IEnumerable<string> recipeIds)
    {
        var idSet = recipeIds.ToHashSet();
        foreach (var item in _allBatchItems) item.IsChecked = idSet.Contains(item.Recipe.Id);
        IsMultiRecipeMode = true;
        RefreshBatchItems();
    }

    /// <summary>검색어에 맞는 항목만 BatchItems에 다시 채운다. 이름뿐 아니라 SearchBlob(태그·
    /// 축 라벨·Labels)까지 훑으므로 "netorare" 같은 실제 태그명으로도 걸린다. _allBatchItems가
    /// 마스터라 체크 상태는 SelectableRecipeItem 인스턴스가 그대로 재사용되면서 유지된다.</summary>
    private void RefreshBatchItems()
    {
        BatchItems.Clear();
        var search = BatchSearchText.Trim();
        foreach (var item in _allBatchItems)
            if (string.IsNullOrWhiteSpace(search) || item.SearchBlob.Contains(search, StringComparison.OrdinalIgnoreCase))
                BatchItems.Add(item);
    }

    /// <summary>현재 검색으로 걸러진 목록만 일괄 체크/해제한다(검색으로 가려진 항목은 안 건드림).</summary>
    [RelayCommand]
    private void SelectAllBatchItems()
    {
        foreach (var item in BatchItems) item.IsChecked = true;
    }

    [RelayCommand]
    private void DeselectAllBatchItems()
    {
        foreach (var item in BatchItems) item.IsChecked = false;
    }

    /// <summary>NSFW(🔞)만 일괄 체크. 기존 체크는 유지하지 않고 이 그룹만 남긴다 —
    /// "NSFW 전체 돌려보기"가 주 용도라 매번 해제부터 누르지 않아도 되게.
    /// 검색 필터가 걸려 있으면 그 안에서만 동작한다(전체 선택 버튼과 같은 규칙).</summary>
    [RelayCommand]
    private void SelectNsfwBatchItems() => SelectByRating(nsfw: true);

    /// <summary>SFW(🔞 아님)만 일괄 체크.</summary>
    [RelayCommand]
    private void SelectSfwBatchItems() => SelectByRating(nsfw: false);

    private void SelectByRating(bool nsfw)
    {
        foreach (var item in BatchItems)
            item.IsChecked = !item.IsPreview && item.IsNsfw == nsfw;
    }

    /// <summary>재시작해도 생성 탭 설정(출력 경로, 줄 수, 체크박스들)이 남아있도록 저장해둔 값을 복원.
    /// 이전엔 Settings에 저장하는 코드 자체가 없어 재시작하면 항상 기본값으로 돌아갔다.</summary>
    private void LoadSettings()
    {
        var s = _main.Settings;
        OutputPath = s.LastOutputPath;
        LineCount = s.DefaultLineCount;
        Mode = s.LastMode;
        DedupeWithinLine = s.DedupeWithinLine;
        AvoidDuplicateLines = s.AvoidDuplicateLines;
        AvoidConflicts = s.AvoidConflicts;
        InsertBlankLine = s.InsertBlankLine;
        UnderscoreToSpace = s.UnderscoreToSpace;
        WeightedSampling = s.WeightedSampling;
        AutoOrderTags = s.AutoOrderTags;
        QualityTagsEnabled = s.QualityTagsEnabled;
        QualityTagsText = s.QualityTagsText;
    }

    /// <summary>현재 생성 탭 설정을 Settings에 담아 디스크에 저장한다.
    /// 생성 성공 직후와 앱 종료 시(MainWindow의 Closing) 둘 다에서 호출된다.</summary>
    public void SaveSettings()
    {
        var s = _main.Settings;
        s.LastOutputPath = OutputPath;
        s.DefaultLineCount = LineCount;
        s.LastMode = Mode;
        s.DedupeWithinLine = DedupeWithinLine;
        s.AvoidDuplicateLines = AvoidDuplicateLines;
        s.AvoidConflicts = AvoidConflicts;
        s.InsertBlankLine = InsertBlankLine;
        s.UnderscoreToSpace = UnderscoreToSpace;
        s.WeightedSampling = WeightedSampling;
        s.AutoOrderTags = AutoOrderTags;
        s.QualityTagsEnabled = QualityTagsEnabled;
        s.QualityTagsText = QualityTagsText;
        SettingsStore.Save(s);
    }

    private GenerationOptions BuildOptions() => new()
    {
        LineCount = LineCount,
        // -1이나 빈 칸(파싱 실패)이면 null → Run()에서 매번 새 랜덤 시드를 뽑는다.
        Seed = int.TryParse(SeedText, out var s) && s != -1 ? s : null,
        DedupeWithinLine = DedupeWithinLine,
        AvoidDuplicateLines = AvoidDuplicateLines,
        AvoidConflicts = AvoidConflicts,
        UnderscoreToSpace = UnderscoreToSpace,
        Sampling = WeightedSampling ? SamplingMode.Weighted : SamplingMode.Uniform,
        AutoOrderTags = AutoOrderTags,
    };

    /// <summary>이번 호출에 실제로 쓰인 시드. 자동(랜덤) 시드일 때도 결과에 남겨 사용자가
    /// "정말 매번 다른 시드로 도는지" 눈으로 확인할 수 있게 한다.</summary>
    private int _lastSeedUsed;

    /// <summary>백그라운드로 넘길 작업 스냅샷. 슬롯·풀은 ObservableCollection이라 생성이
    /// 도는 동안 빌더나 라이브러리 창에서 태그를 고치면 백그라운드 읽기와 겹쳐 깨진다 —
    /// JSON 왕복으로 깊은 복사를 떠서 넘긴다(체크한 레시피만 복사하므로 비용이 제한적).</summary>
    private sealed record GenerationJob(
        IReadOnlyList<Recipe> Recipes,
        IReadOnlyDictionary<string, Pool> Pools,
        GenerationOptions Options,
        int LineCountEach,
        int Seed);

    private static Recipe Clone(Recipe recipe) =>
        System.Text.Json.JsonSerializer.Deserialize<Recipe>(
            System.Text.Json.JsonSerializer.Serialize(recipe, JsonStore.Options), JsonStore.Options)!;

    /// <summary>UI 스레드에서만 할 수 있는 일(슬롯·체크 상태·풀 읽기, 시드 확정)을 먼저 끝내
    /// 백그라운드가 건드릴 게 없는 스냅샷으로 만든다.</summary>
    private GenerationJob BuildJob(int lineCountEach)
    {
        var opts = BuildOptions();
        // 시드를 비워뒀으면(자동) 여기서 직접 하나 뽑아 못박는다 — SystemRandomSource에 null을
        // 넘기면 내부에서 알아서 뽑긴 하지만 그 값을 밖에서 확인할 방법이 없어진다.
        _lastSeedUsed = opts.Seed ?? Random.Shared.Next();
        var recipes = IsMultiRecipeMode
            ? BatchItems.Where(b => b.IsChecked).Select(b => Clone(b.Recipe)).ToList()
            : new List<Recipe> { Clone(_main.RecipeBuilder.BuildRecipe()) };
        return new GenerationJob(recipes, _main.Pools.ToDictionary(p => p.Id), opts, lineCountEach, _lastSeedUsed);
    }

    /// <summary>레시피마다 lineCountEach줄씩 순서대로 뽑아 하나로 잇는다(레시피별로 묶여
    /// 나오도록 — 랜덤 셔플 아님). 레시피마다 시드를 base+순번으로 달리해 서로 다른 레시피가
    /// 완전히 같은 난수 시퀀스를 타지 않게 한다. 모순 줄 인덱스는 이어붙인 뒤의 전체 위치로
    /// 보정한다. UI를 전혀 건드리지 않으므로 백그라운드 스레드에서 그대로 돌 수 있다.</summary>
    private GenerationResult RunJob(GenerationJob job, IProgress<int> progress, CancellationToken token)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var conflicts = new List<LineConflict>();
        int done = 0;

        for (int i = 0; i < job.Recipes.Count; i++)
        {
            var opts = CopyWith(job.Options, job.LineCountEach, unchecked(job.Seed + i));
            int offset = lines.Count;
            int completedBefore = done;
            var inner = new Progress<int>(n => progress.Report(completedBefore + n));

            var result = _generator.Generate(job.Recipes[i], job.Pools, opts, _main.Conflicts, _main.TagInfo, inner, token);

            foreach (var c in result.Conflicts) conflicts.Add(c with { LineIndex = c.LineIndex + offset });
            lines.AddRange(result.Lines);
            foreach (var w in result.Warnings)
                warnings.Add(job.Recipes.Count > 1 ? $"[{job.Recipes[i].Name}] {w}" : w);
            done += result.Lines.Count;
        }

        return new GenerationResult(lines, warnings) { Conflicts = conflicts };
    }

    /// <summary>줄 수와 시드만 바꾼 사본. 백그라운드에서 뷰모델 속성을 다시 읽지 않으려고
    /// 스냅샷의 값만으로 만든다.</summary>
    private static GenerationOptions CopyWith(GenerationOptions o, int lineCount, int seed) => new()
    {
        LineCount = lineCount,
        Seed = seed,
        DedupeWithinLine = o.DedupeWithinLine,
        AvoidDuplicateLines = o.AvoidDuplicateLines,
        AvoidConflicts = o.AvoidConflicts,
        UnderscoreToSpace = o.UnderscoreToSpace,
        Sampling = o.Sampling,
        AutoOrderTags = o.AutoOrderTags,
        Blocklist = o.Blocklist,
    };

    /// <summary>스냅샷을 백그라운드에서 돌리고 진행률을 UI로 보고한다. 예전엔 생성이 UI
    /// 스레드에서 통째로 돌아, 레시피 수십 개 × 수백 줄이면 창이 몇 초씩 응답하지 않았다.</summary>
    private async Task<GenerationResult> RunInBackgroundAsync(GenerationJob job, CancellationToken token)
    {
        long total = (long)job.Recipes.Count * job.LineCountEach;
        // Progress<T>는 만들어진 스레드(여기선 UI)의 컨텍스트로 콜백을 돌려준다.
        var progress = new Progress<int>(n =>
            ProgressText = total > 0 ? $"{n:N0}/{total:N0}줄 ({n * 100 / total}%)" : "");
        return await Task.Run(() => RunJob(job, progress, token), token);
    }

    /// <summary>QualityTagsEnabled가 켜져 있으면 QualityTagsText를 매 줄 맨 앞에 붙인다.
    /// 줄 수·모순 인덱스는 그대로 두고 텍스트만 바꾸므로 Conflicts의 LineIndex는 영향받지 않는다.</summary>
    private GenerationResult ApplyQualityTags(GenerationResult result)
    {
        if (!QualityTagsEnabled || string.IsNullOrWhiteSpace(QualityTagsText)) return result;
        var prefix = QualityTagsText.Trim();
        var prefixed = result.Lines.Select(l => string.IsNullOrEmpty(l) ? l : $"{prefix}, {l}").ToList();
        return result with { Lines = prefixed };
    }

    /// <summary>결과의 모순 줄들을 사람이 읽을 요약으로. 없으면 빈 문자열.</summary>
    private static string BuildConflictReport(GenerationResult result)
    {
        if (result.Conflicts.Count == 0) return "";
        var lines = result.Conflicts.Take(20).Select(c =>
            $"· {c.LineIndex + 1}번째 줄: " +
            string.Join("; ", c.Hits.Select(h => $"{h.Label}({string.Join(" + ", h.Tags)})")));
        var more = result.Conflicts.Count > 20 ? $"\n…외 {result.Conflicts.Count - 20}줄 더" : "";
        return $"⚠ 모순 {result.Conflicts.Count}줄:\n" + string.Join("\n", lines) + more;
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "와일드카드 저장 위치",
            Filter = "텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = string.IsNullOrWhiteSpace(OutputPath) ? "wildcard.txt" : System.IO.Path.GetFileName(OutputPath),
            OverwritePrompt = false, // append 모드 지원 위해 덮어쓰기 경고 끔
        };
        if (!string.IsNullOrWhiteSpace(_main.Settings.LastOutputDir) && System.IO.Directory.Exists(_main.Settings.LastOutputDir))
            dlg.InitialDirectory = _main.Settings.LastOutputDir;
        if (dlg.ShowDialog() == true) OutputPath = dlg.FileName;
    }

    /// <summary>여러 레시피 모드인데 체크된 게 하나도 없으면 상태 메시지만 남기고 취소.
    /// true를 반환하면(막혔으면) 호출부는 더 진행하지 않는다.</summary>
    private bool MultiModeBlockedWithNoSelection()
    {
        if (!IsMultiRecipeMode || BatchItems.Any(b => b.IsChecked)) return false;
        _main.Status = "일괄 생성할 레시피를 하나 이상 체크하세요.";
        return true;
    }

    /// <summary>돌고 있는 쪽(미리보기든 생성이든)을 중단한다. 둘 다 같은 진행률 표시를 쓰므로
    /// 버튼도 하나로 합쳤다.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (PreviewCommand.CanBeCanceled) PreviewCommand.Cancel();
        if (GenerateCommand.CanBeCanceled) GenerateCommand.Cancel();
    }

    [RelayCommand]
    private async Task Preview(CancellationToken token)
    {
        if (MultiModeBlockedWithNoSelection()) return;
        try
        {
            var result = ApplyQualityTags(await RunInBackgroundAsync(BuildJob(Math.Min(20, LineCount)), token));
            PreviewText = string.Join("\n", result.Lines);
            ConflictReport = BuildConflictReport(result);
            var seedNote = $"(시드 {_lastSeedUsed})";
            _main.Status = (result.Warnings.Count > 0 ? string.Join(" / ", result.Warnings) : "미리보기 완료") + " " + seedNote;
        }
        catch (OperationCanceledException) { _main.Status = "미리보기 취소됨"; }
        catch (GenerationValidationException ex) { _main.Status = "검증 오류: " + ex.Message; }
        finally { ProgressText = ""; }
    }

    [RelayCommand]
    private async Task Generate(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(OutputPath)) { _main.Status = "출력 경로를 지정하세요."; return; }
        if (MultiModeBlockedWithNoSelection()) return;
        try
        {
            var result = ApplyQualityTags(await RunInBackgroundAsync(BuildJob(LineCount), token));

            // 파일 쓰기도 백그라운드로. 취소된 뒤엔 아예 쓰지 않는다(파일은 그대로 남는다).
            var (path, mode, blank) = (OutputPath, Mode, InsertBlankLine);
            await Task.Run(() => WildcardWriter.Write(path, result.Lines, mode, blank), token);

            _main.Settings.LastOutputDir = Path.GetDirectoryName(OutputPath) ?? "";
            SaveSettings();
            ConflictReport = BuildConflictReport(result);
            _main.Status = $"{result.Lines.Count}줄 {Mode} 완료 → {OutputPath} (시드 {_lastSeedUsed})"
                + (result.Warnings.Count > 0 ? " (" + string.Join(", ", result.Warnings) + ")" : "");
        }
        catch (OperationCanceledException) { _main.Status = "생성 취소됨 — 파일은 건드리지 않았습니다."; }
        catch (GenerationValidationException ex) { _main.Status = "검증 오류: " + ex.Message; }
        catch (IOException ex) { _main.Status = "파일 오류: " + ex.Message; }
        finally { ProgressText = ""; }
    }
}
