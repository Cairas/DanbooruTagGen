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
    /// <summary>이 레시피만 따로 정한 줄 수(빈 칸 = 생성 탭의 '줄 수'). 숫자로 안 읽히거나 0 이하면 따로 정하지 않은 것으로 본다.</summary>
    [ObservableProperty] private string _lineCountText = "";
    public int? LineCountOverride => int.TryParse(LineCountText.Trim(), out var n) && n > 0 ? n : null;
    /// <summary>줄 수 칸이 비었는가(비었으면 기본값을 회색으로 겹쳐 보여 준다).</summary>
    public bool IsLineCountEmpty => string.IsNullOrWhiteSpace(LineCountText);
    partial void OnLineCountTextChanged(string value) => OnPropertyChanged(nameof(IsLineCountEmpty));

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

    /// <summary>체크된 총 개수와 그중 현재 검색에 가려진 개수. 일괄 생성은 검색과 무관하게
    /// 체크된 전부를 돌리므로(마스터 목록 기준), 화면에 안 보이는 체크가 있다는 사실이
    /// 사용자에게 보여야 한다 — 안 보이면 "왜 예상보다 많이 나왔지"가 된다.</summary>
    [ObservableProperty] private string _checkedCountText = "체크 없음";

    /// <summary>메인 화면에 보여 줄 훅 적용 요약. 훅은 별도 창에 있어서 켜 둔 걸 잊은 채
    /// 생성하면 "왜 태그가 더 붙지"가 된다 — 생성 패널에서 항상 보이게 한다.</summary>
    [ObservableProperty] private string _hookSummaryText = "훅 없음";

    /// <summary>프리셋 콤보에 띄울 목록. MainViewModel.BatchPresets를 이름순으로 비춘다.</summary>
    public ObservableCollection<BatchSelectionPreset> BatchPresets { get; } = new();
    [ObservableProperty] private BatchSelectionPreset? _selectedBatchPreset;
    /// <summary>"현재 선택 저장"에 쓸 이름 입력칸.</summary>
    [ObservableProperty] private string _batchPresetNameInput = "";

    [ObservableProperty] private int _lineCount = 100;
    /// <summary>"-1"은 "매번 랜덤"을 뜻하는 값(빈 문자열도 같은 뜻으로 계속 받아들임).
    /// 빈 칸으로 두면 "설정을 깜빡한 건가 랜덤인 건가" 헷갈릴 수 있어, 기본값 자체를
    /// -1로 명시해 화면에 보이게 했다.</summary>
    [ObservableProperty] private string _seedText = "-1";
    /// <summary>시드 자동일 때 미리보기 시드를 다음 생성에 이어 쓴다(<see cref="SeedCarry"/>).</summary>
    private readonly SeedCarry _seedCarry = new();
    /// <summary>이번 실행의 시드가 직전 미리보기에서 이어 쓴 것인지(상태 문구용).</summary>
    private bool _lastSeedCarried;
    partial void OnSeedTextChanged(string value) => _seedCarry.Reset();
    [ObservableProperty] private bool _dedupeWithinLine = true;
    [ObservableProperty] private bool _avoidDuplicateLines = true;
    [ObservableProperty] private bool _avoidConflicts = true;
    /// <summary>모순 회피를 슬롯 단위로 먼저(GenerationOptions.LocalConflictFiltering). 기본 꺼짐.</summary>
    [ObservableProperty] private bool _localConflictFiltering;
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
    /// <summary>일괄 생성 결과를 한 파일로 잇지 않고 레시피마다 따로 쓴다(출력 경로의 폴더에
    /// "레시피 이름.txt"). ComfyUI에서 컨셉별로 __와일드카드__를 부르려면 팩당 파일이 필요하다.</summary>
    [ObservableProperty] private bool _splitFilesPerRecipe;
    /// <summary>출력 프로필(모델별 출력 설정 묶음). 고르면 품질 태그·'_'→공백·빈 줄·자동 정렬을 한 번에 바꾼다.</summary>
    public ObservableCollection<OutputProfile> OutputProfiles { get; } = new();
    [ObservableProperty] private OutputProfile? _selectedOutputProfile;
    [ObservableProperty] private string _outputProfileName = "";

    /// <summary>시작할 때 지난번 고른 프로필을 콤보에만 다시 띄운다(값은 지난번 설정 그대로 — 프로필 값으로 덮지 않는다).</summary>
    private bool _restoringProfile;

    partial void OnSelectedOutputProfileChanged(OutputProfile? value)
    {
        if (value is null || _restoringProfile) return;
        QualityTagsEnabled = value.QualityTagsEnabled;
        QualityTagsText = value.QualityTagsText;
        UnderscoreToSpace = value.UnderscoreToSpace;
        InsertBlankLine = value.InsertBlankLine;
        AutoOrderTags = value.AutoOrderTags;
        OutputProfileName = value.Name;
        ProfileModel = value.Model;
        ProfileTokenLimitText = value.TokenLimit > 0 ? value.TokenLimit.ToString() : "";
        RefreshPreviewPanel();   // 모델·기준이 바뀌면 미리보기의 토큰 수·주황 표시도 바뀐다
        _main.Status = $"출력 프로필 '{value.Name}' 적용";
    }

    // ── 생성 카드(두 탭 공통) ─────────────────────────────────────────────

    /// <summary>미리보기 줄 수: 일괄(레시피 여러 개)은 레시피마다 3줄, 레시피 하나·편집 탭은 10줄 — "몇 줄 확인" 용도.</summary>
    public const int PreviewLinesPerRecipe = 3;
    public const int PreviewLinesSingle = 10;

    /// <summary>카드 머리: 무엇을 생성하는지("대상: 체크한 레시피 12개" / "대상: 레시피 빌더의 지금 구성").</summary>
    [ObservableProperty] private string _targetText = "";
    /// <summary>생성 뒤 요약 한 줄("✓ 12개 레시피 2,350줄 → nsfw.txt · 덮어쓰기 · 시드 …").</summary>
    [ObservableProperty] private string _resultSummaryText = "";
    /// <summary>미리보기 칸(레시피마다 몇 줄, 한 줄로 잘라 보여 주고 토큰 수).</summary>
    public ObservableCollection<PreviewLine> PreviewLines { get; } = new();
    /// <summary>"Anima 토큰 평균 241 · 최대 425 · 기준 512 넘는 줄 0/36". 실제 토크나이저가 없으면 "(추정)".</summary>
    [ObservableProperty] private string _tokenSummaryText = "";
    /// <summary>고급 설정 머리줄(기본값과 다른 항목 요약).</summary>
    [ObservableProperty] private string _advancedSummaryText = "고급 설정";
    /// <summary>생성할 때마다 결과 창을 자동으로 연다(기본 끔 — 매번 뜨면 번거롭다).</summary>
    [ObservableProperty] private bool _autoOpenResults;
    /// <summary>프로필 저장용: 모델(IL/Anima)과 토큰 기준(빈 칸 = 모델 기본값).</summary>
    [ObservableProperty] private Core.Tokenization.ModelKind _profileModel = Core.Tokenization.ModelKind.IL;
    [ObservableProperty] private string _profileTokenLimitText = "";
    public Core.Tokenization.ModelKind[] ModelKinds { get; } = { Core.Tokenization.ModelKind.IL, Core.Tokenization.ModelKind.Anima };

    // 카드의 '토큰 기준'이 곧 지금 적용되는 값 — 바꾸면 미리보기 토큰 수·주황 표시도 바로 바뀐다.
    partial void OnProfileModelChanged(Core.Tokenization.ModelKind value) => RefreshPreviewPanel();
    partial void OnProfileTokenLimitTextChanged(string value) => RefreshPreviewPanel();

    /// <summary>쓰기 방식 라디오(덮어쓰기/이어쓰기/새 파일) ↔ <see cref="Mode"/>.</summary>
    public bool IsOverwrite { get => Mode == WriteMode.Overwrite; set { if (value) Mode = WriteMode.Overwrite; } }
    public bool IsAppend { get => Mode == WriteMode.Append; set { if (value) Mode = WriteMode.Append; } }
    public bool IsNew { get => Mode == WriteMode.New; set { if (value) Mode = WriteMode.New; } }

    partial void OnModeChanged(WriteMode value)
    {
        OnPropertyChanged(nameof(IsOverwrite));
        OnPropertyChanged(nameof(IsAppend));
        OnPropertyChanged(nameof(IsNew));
    }

    partial void OnIsMultiRecipeModeChanged(bool value)
    {
        if (_main.SelectedTabIndex != (value ? 0 : 1)) _main.SelectedTabIndex = value ? 0 : 1;
        RefreshCheckedCount();
    }

    private static string ModeText(WriteMode m) => m switch
    {
        WriteMode.Overwrite => "덮어쓰기",
        WriteMode.Append => "이어쓰기",
        _ => "새 파일",
    };

    /// <summary>고급 설정 머리줄에 요약할 속성들(바뀌면 다시 요약).</summary>
    private static readonly HashSet<string> AdvancedProps = new()
    {
        nameof(SeedText), nameof(DedupeWithinLine), nameof(AvoidDuplicateLines), nameof(StopWhenExhausted), nameof(AvoidConflicts),
        nameof(LocalConflictFiltering), nameof(UnderscoreToSpace), nameof(WeightedSampling), nameof(AutoOrderTags),
        nameof(InsertBlankLine), nameof(QualityTagsEnabled), nameof(AutoOpenResults),
    };

    private void RefreshAdvancedSummary()
    {
        var d = new List<string>();
        if (SeedText.Trim() is { Length: > 0 } s && s != "-1") d.Add($"시드 {s}");
        if (!DedupeWithinLine) d.Add("줄 내 중복 제거 끔");
        if (!AvoidDuplicateLines) d.Add("줄 중복 회피 끔");
        else if (!StopWhenExhausted) d.Add("바닥나면 멈춤 끔");
        if (!AvoidConflicts) d.Add("모순 회피 끔");
        else if (LocalConflictFiltering) d.Add("슬롯 단위 걸러내기");
        if (!UnderscoreToSpace) d.Add("_ 그대로");
        if (WeightedSampling) d.Add("빈도 가중");
        if (AutoOrderTags) d.Add("자동 정렬");
        if (!InsertBlankLine) d.Add("빈 줄 안 띄움");
        if (QualityTagsEnabled) d.Add("품질 태그 켬");
        if (AutoOpenResults) d.Add("결과 창 자동");
        AdvancedSummaryText = d.Count == 0 ? "고급 설정" : $"고급 설정 (기본과 다름: {string.Join(" · ", d.Take(4))}{(d.Count > 4 ? $" 외 {d.Count - 4}" : "")})";
    }

    private Views.ResultsWindow? _resultsWindow;

    /// <summary>결과 크게 보기: 줄 확인·📌 잠금·결과 검색·줄 출처. 버튼으로만 연다(설정으로 자동 열기 가능).</summary>
    [RelayCommand]
    private void OpenResults()
    {
        if (_resultsWindow != null) { _resultsWindow.Activate(); return; }
        var win = new Views.ResultsWindow { DataContext = this, Owner = System.Windows.Application.Current.MainWindow };
        _resultsWindow = win;
        win.Closed += (_, _) => _resultsWindow = null;
        win.Show();
    }

    private void AutoOpenResultsIfWanted()
    {
        if (AutoOpenResults && _resultsWindow == null) OpenResults();
    }

    /// <summary>마지막으로 띄운 결과(미리보기 칸을 다시 그릴 때 — 프로필·품질 태그가 바뀌면 토큰 수가 달라진다).</summary>
    private IReadOnlyList<RecipeOutput> _lastOutputs = Array.Empty<RecipeOutput>();

    /// <summary>미리보기 칸과 토큰 요약을 지금 프로필 기준으로 다시 그린다.</summary>
    private void RefreshPreviewPanel()
    {
        PreviewLines.Clear();
        if (_lastOutputs.Count == 0) { TokenSummaryText = ""; return; }
        var (counter, limit) = CurrentTokenRule();
        bool multi = _lastOutputs.Count > 1;
        int perRecipe = multi ? PreviewLinesPerRecipe : PreviewLinesSingle;
        var allDisplay = new List<string>();
        foreach (var o in _lastOutputs)
        {
            var display = ApplyQualityTags(o.Result).Lines;
            allDisplay.AddRange(display);
            if (multi) PreviewLines.Add(new PreviewLine(o.Recipe.Name, "", "", 0, false));
            foreach (var line in display.Take(perRecipe))
            {
                int t = Core.Tokenization.TokenCounters.CountLine(counter, line);
                PreviewLines.Add(new PreviewLine("", line, line, t, t > limit));
            }
        }
        var len = Core.Output.LineStats.Measure(allDisplay, counter, limit);
        TokenSummaryText = len.LineCount == 0 ? "" :
            $"{len.Model} 토큰 평균 {len.TokensAverage:0.#} · 최대 {len.TokensMax}"
            + (len.Model == Core.Tokenization.ModelKind.IL ? $"({len.ChunksMax}청크)" : "")
            + $" · 기준 {len.TokenLimit} 넘는 줄 {len.OverLimit}/{len.LineCount}"
            + (len.IsExact ? "" : " (추정)");
    }

    /// <summary>지금 출력 설정을 이름칸의 이름으로 저장한다(같은 이름이면 덮어씀).</summary>
    [RelayCommand]
    private void SaveOutputProfile()
    {
        var name = OutputProfileName.Trim();
        if (name.Length == 0) { _main.Status = "출력 프로필 이름을 입력하세요."; return; }
        var profile = OutputProfiles.FirstOrDefault(p => p.Name == name);
        if (profile is null) { profile = new OutputProfile { Name = name }; OutputProfiles.Add(profile); }
        profile.QualityTagsEnabled = QualityTagsEnabled;
        profile.QualityTagsText = QualityTagsText;
        profile.UnderscoreToSpace = UnderscoreToSpace;
        profile.InsertBlankLine = InsertBlankLine;
        profile.AutoOrderTags = AutoOrderTags;
        profile.Model = ProfileModel;
        profile.TokenLimit = ParsedTokenLimit();
        _main.Settings.OutputProfiles = OutputProfiles.ToList();
        // 저장한 프로필을 고른 상태로(같은 값이라 다시 적용해도 그대로) — 다음 실행에도 이 프로필로 시작한다.
        SelectedOutputProfile = profile;
        SaveSettings();
        _main.Status = $"출력 프로필 '{name}' 저장됨 · {ProfileModel} 기준 {CurrentTokenRule().Limit}";
    }

    [RelayCommand]
    private void DeleteOutputProfile()
    {
        if (SelectedOutputProfile is not { } p) { _main.Status = "지울 출력 프로필을 고르세요."; return; }
        OutputProfiles.Remove(p);
        SelectedOutputProfile = null;
        _main.Settings.OutputProfiles = OutputProfiles.ToList();
        SettingsStore.Save(_main.Settings);
        _main.Status = $"출력 프로필 '{p.Name}' 삭제됨";
    }

    /// <summary>서로 다른 줄이 바닥나면 요청 줄 수를 못 채워도 멈춘다(GenerationOptions.StopWhenExhausted).</summary>
    [ObservableProperty] private bool _stopWhenExhausted = true;

    /// <summary>방금 뽑은 줄 목록(미리보기·생성 둘 다). 줄마다 📌 잠금과 출처(<see cref="ResultLineItem.Origin"/>)를 단다.
    /// 통째로 갈아 끼운다 — 줄을 하나씩 Add하면 일괄 생성 수천 줄에서 목록 갱신 알림만 수천 번 난다.</summary>
    [ObservableProperty] private ObservableCollection<ResultLineItem> _resultLines = new();
    /// <summary>뽑은 줄 전부(<see cref="ResultLines"/>는 결과 검색으로 걸러진 표시용). 잠금·복사·다시 뽑기는 늘 이쪽 기준이다 —
    /// 검색으로 가려진 잠근 줄이 다음 생성에서 빠지면 안 된다.</summary>
    private List<ResultLineItem> _allResults = new();
    /// <summary>결과 검색어: 공백으로 나눈 낱말이 전부 들어간 줄만(밑줄·공백 구분 없음, 대소문자 무시). "-낱말"은 그 낱말이 없는 줄.</summary>
    [ObservableProperty] private string _resultFilterText = "";
    /// <summary>"200줄" / 검색 중이면 "12/200줄 · 75토큰 넘는 줄 3".</summary>
    [ObservableProperty] private string _resultCountText = "";
    [ObservableProperty] private ResultLineItem? _selectedResultLine;
    /// <summary>선택한 줄의 출처(슬롯마다 무엇이 나왔는지·무엇이 걸러졌는지). 선택이 없으면 빈 문자열.</summary>
    [ObservableProperty] private string _selectedOriginText = "";
    /// <summary>"📌 3줄 잠김". 잠근 줄이 없으면 빈 문자열(잠금 해제 버튼도 숨는다).</summary>
    [ObservableProperty] private string _lockedCountText = "";

    public WriteMode[] Modes { get; } = { WriteMode.New, WriteMode.Overwrite, WriteMode.Append };

    partial void OnSelectedResultLineChanged(ResultLineItem? value) =>
        SelectedOriginText = value is null ? "" : BuildOriginText(value);

    private static string BuildOriginText(ResultLineItem item)
    {
        var header = $"#{item.Number} · {item.RecipeName}"
            + (item.Origin is { Retries: > 0 } o ? $" · 줄 전체 재추첨 {o.Retries}회" : "")
            + (item.IsLocked ? " · 📌 잠김" : "");
        return item.Origin is null
            ? header
            : header + "\n" + LineOriginText.Format(item.Origin, item.Hits);
    }

    partial void OnResultFilterTextChanged(string value) => ApplyResultFilter();

    private static string NormalizeForSearch(string s) => s.Replace('_', ' ').ToLowerInvariant();

    private void ApplyResultFilter()
    {
        var terms = ResultFilterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeForSearch).ToList();
        var include = terms.Where(t => !t.StartsWith('-')).ToList();
        var exclude = terms.Where(t => t.StartsWith('-') && t.Length > 1).Select(t => t[1..]).ToList();
        var shown = terms.Count == 0
            ? _allResults
            : _allResults.Where(r =>
            {
                var text = NormalizeForSearch(r.DisplayText);
                return include.All(t => text.Contains(t, StringComparison.Ordinal)) && !exclude.Any(t => text.Contains(t, StringComparison.Ordinal));
            }).ToList();
        var keep = SelectedResultLine;
        ResultLines = new ObservableCollection<ResultLineItem>(shown);
        SelectedResultLine = keep != null && shown.Contains(keep) ? keep : null;
        int over = shown.Count(r => r.IsOverLimit);
        ResultCountText = _allResults.Count == 0 ? ""
            : (terms.Count == 0 ? $"{_allResults.Count:N0}줄" : $"{shown.Count:N0}/{_allResults.Count:N0}줄")
              + (over > 0 ? $" · 토큰 기준 넘는 줄 {over:N0}" : "");
    }

    private void RefreshLockedCount()
    {
        int n = _allResults.Count(r => r.IsLocked);
        LockedCountText = n == 0 ? "" : $"📌 {n}줄 잠김";
        if (SelectedResultLine is { } s) SelectedOriginText = BuildOriginText(s);
    }

    [RelayCommand]
    private void UnlockAll()
    {
        foreach (var r in _allResults) r.IsLocked = false;
    }

    /// <summary>선택한 줄 하나만 클립보드로(품질 태그 붙은 그대로).</summary>
    [RelayCommand]
    private void CopySelectedLine()
    {
        if (SelectedResultLine is not { } line) { _main.Status = "복사할 줄을 먼저 고르세요."; return; }
        try
        {
            System.Windows.Clipboard.SetText(line.DisplayText);
            _main.Status = $"{line.Number}번째 줄을 클립보드에 복사했습니다.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _main.Status = "클립보드를 다른 프로그램이 쓰고 있어 복사하지 못했습니다. 잠시 후 다시 시도하세요.";
        }
    }

    /// <summary>결과 목록을 새로 채운다. 레시피마다 잠근 줄이 앞에 오고(<see cref="RecipeOutput.LockedCount"/>) 새 줄이 뒤에 온다.</summary>
    private void ShowResults(IReadOnlyList<RecipeOutput> outputs)
    {
        var (counter, limit) = CurrentTokenRule();
        var items = new List<ResultLineItem>();
        bool multi = outputs.Count > 1;
        foreach (var o in outputs)
        {
            var display = ApplyQualityTags(o.Result).Lines;
            int lineTokens;
            var hits = new Dictionary<int, IReadOnlyList<ConflictHit>>();
            foreach (var c in o.Result.Conflicts) hits[c.LineIndex] = c.Hits;
            for (int j = 0; j < o.Result.Lines.Count; j++)
            {
                var item = new ResultLineItem(
                    items.Count + 1, o.LockKey, o.Recipe.Name,
                    multi && j == 0 ? $"── {o.Recipe.Name} ──" : "",
                    o.Result.Lines[j], display[j],
                    j < o.Result.LineKeys.Count ? o.Result.LineKeys[j] : "",
                    o.Result.Origins is { } origins && j < origins.Count ? origins[j] : null,
                    hits.GetValueOrDefault(j) ?? Array.Empty<ConflictHit>(),
                    isLocked: j < o.LockedCount,
                    tokens: lineTokens = Core.Tokenization.TokenCounters.CountLine(counter, display[j]),
                    overLimit: lineTokens > limit);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ResultLineItem.IsLocked)) RefreshLockedCount();
                };
                items.Add(item);
            }
        }
        _allResults = items;
        SelectedResultLine = null;
        ApplyResultFilter();
        _lastOutputs = outputs;
        RefreshPreviewPanel();
        PreviewText = string.Join("\n", items.Select(i => i.DisplayText));
        RefreshLockedCount();
    }

    /// <summary>잠금 기준 키. 단일 생성은 빌더에 불러온 레시피(바꿔 불러오면 다른 키라 이전 레시피의 잠근 줄이 안 섞인다).</summary>
    private string BuilderLockKey() => "builder:" + (_main.RecipeBuilder.LoadedRecipeId ?? "");

    public GenerationViewModel(MainViewModel main)
    {
        _main = main;
        LoadSettings();
        foreach (var r in OrderedRecipes()) _allBatchItems.Add(CreateBatchItem(r));
        RefreshBatchItems();
        RefreshBatchPresets();
        RefreshHookSummary();
        RefreshAdvancedSummary();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null) return;
            if (AdvancedProps.Contains(e.PropertyName)) RefreshAdvancedSummary();
            // 품질 태그를 켜고 끄면 실제 출력 줄 길이가 달라진다(글자 하나 칠 때마다 다시 세지는 않는다).
            if (e.PropertyName == nameof(QualityTagsEnabled)) RefreshPreviewPanel();
            if (e.PropertyName == nameof(LineCount)) RefreshCheckedCount();
        };
    }

    partial void OnBatchSearchTextChanged(string value) => RefreshBatchItems();

    /// <summary>레시피 라이브러리가 바깥에서 바뀐 뒤(예: 번들 프리셋 갱신, 라벨 편집) 체크
    /// 목록을 다시 채운다. 이미 체크해 둔 레시피는 Id로 찾아 체크 상태를 그대로 옮긴다.</summary>
    public void RefreshBatchRecipes()
    {
        var checkedIds = _allBatchItems.Where(b => b.IsChecked).Select(b => b.Recipe.Id).ToHashSet();
        _allBatchItems.Clear();
        foreach (var r in OrderedRecipes())
        {
            var item = CreateBatchItem(r);
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

    /// <summary>체크리스트에 뿌릴 레시피를 순서대로 준다. 레시피 라이브러리 창과 같은 규칙을
    /// 써야 두 화면의 순서가 어긋나지 않는다 — "테스트" 라벨이 붙은 팩만 맨 아래로 내리고,
    /// 나머지는 recipes.json 배열 순서를 그대로 둔다.</summary>
    private IEnumerable<Recipe> OrderedRecipes() => RecipeDisplayOrder.PinnedLast(_main.SavedRecipes);

    /// <summary>체크 상태가 바뀔 때마다 개수 표시를 갱신해야 해서, 항목 생성을 한 곳으로 모았다.</summary>
    private SelectableRecipeItem CreateBatchItem(Recipe recipe)
    {
        var item = new SelectableRecipeItem(recipe);
        if (_main.Settings.BatchLineCounts.TryGetValue(recipe.Id, out var saved)) item.LineCountText = saved.ToString();
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectableRecipeItem.IsChecked)) RefreshCheckedCount();
            else if (e.PropertyName == nameof(SelectableRecipeItem.LineCountText))
            {
                // 설정은 앱을 닫을 때(SaveSettings) 디스크에 쓴다.
                if (item.LineCountOverride is { } n) _main.Settings.BatchLineCounts[recipe.Id] = n;
                else _main.Settings.BatchLineCounts.Remove(recipe.Id);
                RefreshCheckedCount();
            }
        };
        return item;
    }

    /// <summary>체크 수·예상 줄 수(레시피별 줄 수 반영)·검색에 가려진 체크, 그리고 생성 카드의 대상 문구를 다시 쓴다.</summary>
    private void RefreshCheckedCount()
    {
        var checkedItems = _allBatchItems.Where(b => b.IsChecked).ToList();
        int total = checkedItems.Count;
        int hidden = total - BatchItems.Count(b => b.IsChecked);
        long expected = checkedItems.Sum(b => (long)(b.LineCountOverride ?? LineCount));
        CheckedCountText = total == 0
            ? "체크 없음"
            : $"체크 {total}개 · 예상 {expected:N0}줄" + (hidden > 0 ? $" (검색·보기에 가려진 체크 {hidden}개 포함)" : "");
        TargetText = IsMultiRecipeMode
            ? (total == 0 ? "대상: 체크한 레시피 없음 — 왼쪽 목록에서 고르세요" : $"대상: 체크한 레시피 {total}개")
            : "대상: 레시피 빌더의 지금 구성";
        ChangedFilterText = $"🆕 새로·바뀜 {_allBatchItems.Count(b => _main.ChangeBadgeOf(b.Recipe.Id).Length > 0)}";
    }

    /// <summary>일괄 목록 보기: 0 전체 · 1 일반 · 2 🔞 성인 · 3 🆕 새로·바뀜. 보기는 "무엇을 보여 줄지"만 정한다 — 생성은 체크된 전부.</summary>
    [ObservableProperty] private int _batchFilter;
    [ObservableProperty] private string _changedFilterText = "🆕 새로·바뀜";
    public bool FilterAll { get => BatchFilter == 0; set { if (value) BatchFilter = 0; } }
    public bool FilterSfw { get => BatchFilter == 1; set { if (value) BatchFilter = 1; } }
    public bool FilterNsfw { get => BatchFilter == 2; set { if (value) BatchFilter = 2; } }
    public bool FilterChanged { get => BatchFilter == 3; set { if (value) BatchFilter = 3; } }

    partial void OnBatchFilterChanged(int value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterSfw));
        OnPropertyChanged(nameof(FilterNsfw));
        OnPropertyChanged(nameof(FilterChanged));
        RefreshBatchItems();
    }

    /// <summary>검색어와 보기에 맞는 항목만 BatchItems에 다시 채운다. 이름뿐 아니라 SearchBlob(태그·
    /// 축 라벨·Labels)까지 훑으므로 "netorare" 같은 실제 태그명으로도 걸린다. _allBatchItems가
    /// 마스터라 체크 상태는 SelectableRecipeItem 인스턴스가 그대로 재사용되면서 유지된다.</summary>
    private void RefreshBatchItems()
    {
        BatchItems.Clear();
        var search = BatchSearchText.Trim();
        foreach (var item in _allBatchItems)
        {
            if (!string.IsNullOrWhiteSpace(search) && !item.SearchBlob.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            bool show = BatchFilter switch
            {
                1 => !item.IsNsfw,
                2 => item.IsNsfw,
                3 => _main.ChangeBadgeOf(item.Recipe.Id).Length > 0,
                _ => true,
            };
            if (show) BatchItems.Add(item);
        }
        // 가려진 개수는 검색어·보기에 따라 달라지므로 목록을 다시 채울 때마다 계산한다.
        RefreshCheckedCount();
    }

    /// <summary>지금 보이는 것만 체크한다(검색·보기로 가려진 항목은 안 건드림). 일반/성인 보기에선 "프리뷰" 라벨 팩
    /// (포즈 레퍼런스 샷)은 빼고 체크한다 — 예전 "🔞 전체"/"SFW 전체" 버튼과 같은 규칙.</summary>
    [RelayCommand]
    private void SelectAllBatchItems()
    {
        foreach (var item in BatchItems) item.IsChecked = BatchFilter is 1 or 2 ? !item.IsPreview : true;
    }

    [RelayCommand]
    private void DeselectAllBatchItems()
    {
        foreach (var item in BatchItems) item.IsChecked = false;
    }

    /// <summary>훅 창에서 변경이 있을 때 HookLibraryViewModel이 호출한다.</summary>
    public void RefreshHookSummary()
    {
        int on = _main.Hooks.Count(h => h.IsEnabled);
        HookSummaryText = on == 0 ? "" : $"🔗 훅 {on}개 적용 중 — 생성할 때 레시피에 끼워 넣습니다";
    }

    private void RefreshBatchPresets()
    {
        var previous = SelectedBatchPreset;
        BatchPresets.Clear();
        foreach (var p in _main.BatchPresets.OrderBy(p => p.Name, StringComparer.CurrentCulture))
            BatchPresets.Add(p);
        // 목록을 다시 채우면 콤보 선택이 풀린다 — 아직 살아 있는 프리셋이면 되돌려 준다.
        if (previous != null && BatchPresets.Contains(previous)) SelectedBatchPreset = previous;
    }

    /// <summary>프리셋의 레시피들을 체크한다. 저장 이후 사라진 레시피는 BatchSelectionResolver가
    /// 걸러 내며, 라이브러리 자체가 비정상으로 보이면 정리를 보류하고 경고만 남긴다.</summary>
    [RelayCommand]
    private void LoadBatchPreset()
    {
        if (SelectedBatchPreset is not { } preset)
        {
            _main.Status = "불러올 프리셋을 고르세요.";
            return;
        }

        var resolution = BatchSelectionResolver.Resolve(preset, _main.SavedRecipes);

        // 사라진 항목을 정리했거나 이름 폴백으로 id를 갱신했으면 저장한다.
        // 안전장치가 발동한 경우 ShouldSave는 false라 프리셋 파일을 건드리지 않는다.
        if (resolution.ShouldSave)
        {
            preset.Entries = resolution.Entries.ToList();
            preset.SavedAt = DateTime.Now;
            _main.SaveBatchPresets();
        }

        var idSet = resolution.MatchedRecipeIds.ToHashSet(StringComparer.Ordinal);
        foreach (var item in _allBatchItems) item.IsChecked = idSet.Contains(item.Recipe.Id);
        IsMultiRecipeMode = true;
        RefreshBatchItems();

        if (resolution.MissingNames.Count == 0)
        {
            _main.Status = $"프리셋 '{preset.Name}' 불러옴 — {resolution.MatchedRecipeIds.Count}개 선택됨.";
            return;
        }

        var names = string.Join(", ", resolution.MissingNames.Take(5))
                    + (resolution.MissingNames.Count > 5 ? $" 외 {resolution.MissingNames.Count - 5}개" : "");

        _main.Status = resolution.PruningWithheld
            // 라이브러리가 비었거나 절반 이상이 안 맞는다 — 지금 정리하면 프리셋이 사실상 사라진다.
            ? $"⚠ 프리셋 '{preset.Name}': {resolution.MissingNames.Count}개를 찾지 못했습니다({names}). "
              + "라이브러리가 정상인지 확인하세요 — 프리셋은 그대로 두었습니다."
            : $"프리셋 '{preset.Name}' 불러옴 — {resolution.MatchedRecipeIds.Count}개 선택됨. "
              + $"사라진 {resolution.MissingNames.Count}개 정리됨: {names}";
    }

    /// <summary>지금 체크된 레시피들을 프리셋 엔트리로 만든다. 검색으로 가려진 체크도 포함한다
    /// — 생성이 그 기준으로 도므로 저장도 같은 기준이어야 한다.</summary>
    private List<BatchSelectionEntry> CheckedEntries() => _allBatchItems
        .Where(b => b.IsChecked)
        .Select(b => new BatchSelectionEntry { RecipeId = b.Recipe.Id, RecipeName = b.Recipe.Name })
        .ToList();

    /// <summary>고른 프리셋을 지금 체크된 레시피들로 덮어쓴다(이름 유지).
    /// <para>"새로 저장"과 버튼을 나눈 이유: 하나로 합치면 이름칸을 고친 채 저장했을 때
    /// "이름 변경"인지 "새 프리셋"인지 의도가 갈린다. 버튼이 둘이면 섞일 여지가 없다.</para></summary>
    [RelayCommand]
    private void UpdateBatchPreset()
    {
        if (SelectedBatchPreset is not { } preset)
        {
            _main.Status = "업데이트할 프리셋을 고르세요.";
            return;
        }

        var entries = CheckedEntries();
        if (entries.Count == 0)
        {
            _main.Status = "체크된 레시피가 없습니다. 업데이트할 선택이 없습니다.";
            return;
        }

        // 덮어쓰면 이전 구성은 사라지므로 개수를 함께 보여 주고 확인받는다.
        var answer = System.Windows.MessageBox.Show(
            $"프리셋 '{preset.Name}'({preset.Entries.Count}개)을 지금 체크된 {entries.Count}개로 덮어쓸까요?",
            "프리셋 업데이트", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        preset.Entries = entries;
        preset.SavedAt = DateTime.Now;
        _main.SaveBatchPresets();
        _main.Status = $"프리셋 '{preset.Name}' 업데이트됨 — {entries.Count}개.";
    }

    /// <summary>이름칸의 이름으로 새 프리셋을 만든다. 같은 이름이 이미 있으면 확인 후 덮어쓴다.</summary>
    [RelayCommand]
    private void SaveBatchPreset()
    {
        var name = BatchPresetNameInput.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _main.Status = "프리셋 이름을 입력하세요.";
            return;
        }

        var entries = CheckedEntries();
        if (entries.Count == 0)
        {
            _main.Status = "체크된 레시피가 없습니다. 저장할 선택이 없습니다.";
            return;
        }

        var existing = _main.BatchPresets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
        {
            var answer = System.Windows.MessageBox.Show(
                $"'{name}' 프리셋이 이미 있습니다. 덮어쓸까요?", "프리셋 덮어쓰기",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            existing.Entries = entries;
            existing.SavedAt = DateTime.Now;
        }
        else
        {
            _main.BatchPresets.Add(new BatchSelectionPreset { Name = name, Entries = entries });
        }

        _main.SaveBatchPresets();
        RefreshBatchPresets();
        SelectedBatchPreset = BatchPresets.FirstOrDefault(p => p.Name == name);
        BatchPresetNameInput = "";
        _main.Status = $"프리셋 '{name}' 저장됨 — {entries.Count}개.";
    }

    /// <summary>[저장 ▾] → "지금 체크로 새로 저장…": 이름을 묻고 새 프리셋으로 저장한다(같은 이름이면 덮어쓸지 묻는다).</summary>
    [RelayCommand]
    private void SaveBatchPresetAs()
    {
        var name = Views.NamePromptWindow.Ask("선택 프리셋 저장", "지금 체크된 레시피를 이 이름으로 저장합니다:",
            SelectedBatchPreset?.Name ?? "");
        if (name is null) return;
        BatchPresetNameInput = name;
        SaveBatchPreset();
    }

    [RelayCommand]
    private void DeleteBatchPreset()
    {
        if (SelectedBatchPreset is not { } preset)
        {
            _main.Status = "삭제할 프리셋을 고르세요.";
            return;
        }
        var answer = System.Windows.MessageBox.Show(
            $"프리셋 '{preset.Name}'을 삭제할까요?", "프리셋 삭제",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        _main.BatchPresets.Remove(preset);
        _main.SaveBatchPresets();
        SelectedBatchPreset = null;
        RefreshBatchPresets();
        _main.Status = $"프리셋 '{preset.Name}' 삭제됨.";
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
        LocalConflictFiltering = s.LocalConflictFiltering;
        InsertBlankLine = s.InsertBlankLine;
        UnderscoreToSpace = s.UnderscoreToSpace;
        WeightedSampling = s.WeightedSampling;
        AutoOrderTags = s.AutoOrderTags;
        QualityTagsEnabled = s.QualityTagsEnabled;
        QualityTagsText = s.QualityTagsText;
        SplitFilesPerRecipe = s.SplitFilesPerRecipe;
        StopWhenExhausted = s.StopWhenExhausted;
        AutoOpenResults = s.AutoOpenResults;
        foreach (var p in s.OutputProfiles) OutputProfiles.Add(p);
        ProfileModel = s.OutputModel;
        ProfileTokenLimitText = s.OutputTokenLimit > 0 ? s.OutputTokenLimit.ToString() : "";
        if (OutputProfiles.FirstOrDefault(p => p.Name == s.LastOutputProfileName) is { } last)
        {
            _restoringProfile = true;
            try { SelectedOutputProfile = last; OutputProfileName = last.Name; }
            finally { _restoringProfile = false; }
        }
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
        s.LocalConflictFiltering = LocalConflictFiltering;
        s.InsertBlankLine = InsertBlankLine;
        s.UnderscoreToSpace = UnderscoreToSpace;
        s.WeightedSampling = WeightedSampling;
        s.AutoOrderTags = AutoOrderTags;
        s.QualityTagsEnabled = QualityTagsEnabled;
        s.QualityTagsText = QualityTagsText;
        s.SplitFilesPerRecipe = SplitFilesPerRecipe;
        s.StopWhenExhausted = StopWhenExhausted;
        s.AutoOpenResults = AutoOpenResults;
        s.OutputModel = ProfileModel;
        s.OutputTokenLimit = ParsedTokenLimit();
        s.LastOutputProfileName = SelectedOutputProfile?.Name ?? "";
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
        LocalConflictFiltering = LocalConflictFiltering,
        UnderscoreToSpace = UnderscoreToSpace,
        Sampling = WeightedSampling ? SamplingMode.Weighted : SamplingMode.Uniform,
        AutoOrderTags = AutoOrderTags,
        StopWhenExhausted = StopWhenExhausted,
    };

    /// <summary>생성 탭 설정 그대로의 옵션(실시간 지표·레시피 비교용). 시드가 자동이면 12345로 고정한다 —
    /// 고칠 때마다 지표가 난수 때문에 흔들리면 "고친 효과"와 "운"을 구분할 수 없다.</summary>
    public GenerationOptions SnapshotOptions()
    {
        var o = BuildOptions();
        o.Seed ??= 12345;
        return o;
    }

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
        IReadOnlyList<int> LineCounts,
        int Seed,
        IReadOnlyList<string> LockKeys,
        IReadOnlyList<IReadOnlyList<LockedLine>> Locked,
        int DroppedLocks);

    /// <summary>잠근 줄의 스냅샷(백그라운드로 넘기므로 뷰모델 객체 대신 값만).</summary>
    private sealed record LockedLine(string RawText, string LineKey, LineOrigin? Origin, IReadOnlyList<ConflictHit> Hits);

    /// <summary>잠근 줄 + 새로 뽑은 줄을 한 결과로. 잠근 줄의 모순은 그 줄이 처음 나왔을 때의 기록을 그대로 쓴다.</summary>
    private static GenerationResult Compose(IReadOnlyList<LockedLine> locked, GenerationResult fresh)
    {
        if (locked.Count == 0) return fresh;
        var conflicts = new List<LineConflict>();
        for (int i = 0; i < locked.Count; i++)
            if (locked[i].Hits.Count > 0) conflicts.Add(new LineConflict(i, locked[i].Hits));
        conflicts.AddRange(fresh.Conflicts.Select(c => c with { LineIndex = c.LineIndex + locked.Count }));
        var empty = new LineOrigin(Array.Empty<SlotOrigin>(), 0);
        return fresh with
        {
            Lines = locked.Select(l => l.RawText).Concat(fresh.Lines).ToList(),
            LineKeys = locked.Select(l => l.LineKey).Concat(fresh.LineKeys).ToList(),
            Origins = fresh.Origins is null ? null : locked.Select(l => l.Origin ?? empty).Concat(fresh.Origins).ToList(),
            Conflicts = conflicts,
        };
    }

    private static Recipe Clone(Recipe recipe) =>
        System.Text.Json.JsonSerializer.Deserialize<Recipe>(
            System.Text.Json.JsonSerializer.Serialize(recipe, JsonStore.Options), JsonStore.Options)!;

    /// <summary>후보가 없어 건너뛴 훅의 이름들. 생성이 끝난 뒤 상태 메시지로 알린다 —
    /// 조용히 빠지면 "훅을 켰는데 왜 안 나오지"가 된다.</summary>
    private IReadOnlyList<string> _skippedHooks = Array.Empty<string>();

    /// <summary>UI 스레드에서만 할 수 있는 일(슬롯·체크 상태·풀 읽기, 시드 확정)을 먼저 끝내
    /// 백그라운드가 건드릴 게 없는 스냅샷으로 만든다.</summary>
    /// <param name="preview">미리보기면 레시피마다 최대 20줄.</param>
    private GenerationJob BuildJob(bool preview)
    {
        var opts = BuildOptions();
        // 시드를 비워뒀으면(자동) 여기서 직접 하나 뽑아 못박는다 — SystemRandomSource에 null을
        // 넘기면 내부에서 알아서 뽑긴 하지만 그 값을 밖에서 확인할 방법이 없어진다.
        // 자동 시드면 [생성]은 직전 미리보기 시드를 이어 쓴다 — 미리보기에서 본 줄이 파일 앞쪽에 그대로 들어간다.
        (_lastSeedUsed, _lastSeedCarried) = _seedCarry.Next(opts.Seed, preview, Random.Shared.Next);
        // 마스터 목록을 읽는다. BatchItems는 검색어로 걸러진 표시용이라, 레시피를 체크해 둔 뒤
        // 검색창에 무언가를 입력하면 가려진 항목이 조용히 생성에서 빠졌다(성공으로 끝나므로
        // 눈치채기도 어려웠다). 검색은 "무엇을 보여줄지"만 정해야지 "무엇을 생성할지"를
        // 정해서는 안 된다.
        var sources = IsMultiRecipeMode
            ? _allBatchItems.Where(b => b.IsChecked).Select(b => (Recipe: b.Recipe, Key: b.Recipe.Id, Lines: b.LineCountOverride ?? LineCount)).ToList()
            : new List<(Recipe Recipe, string Key, int Lines)> { (_main.RecipeBuilder.BuildRecipeForRun(), BuilderLockKey(), LineCount) };
        var recipes = sources.Select(s => Clone(s.Recipe)).ToList();
        var lockKeys = sources.Select(s => s.Key).ToList();

        // 잠근 줄: 같은 키의 레시피를 다시 뽑을 때만 남는다. 이번 대상이 아닌 레시피의 잠근 줄은 목록에서 빠진다(알림).
        var lockedByKey = _allResults.Where(r => r.IsLocked)
            .GroupBy(r => r.LockKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LockedLine>)g.Select(r => new LockedLine(r.RawText, r.LineKey, r.Origin, r.Hits)).ToList(), StringComparer.Ordinal);
        var locked = lockKeys.Select(k => lockedByKey.GetValueOrDefault(k) ?? Array.Empty<LockedLine>()).ToList();
        int droppedLocks = lockedByKey.Where(kv => !lockKeys.Contains(kv.Key)).Sum(kv => kv.Value.Count);

        // 미리보기는 "몇 줄 확인"용: 레시피 여러 개면 레시피마다 3줄, 하나면 10줄(잠근 줄은 그 밖에 따로).
        int previewEach = sources.Count > 1 ? PreviewLinesPerRecipe : PreviewLinesSingle;
        var lineCounts = sources.Select((s, i) => RunLineCounts.Total(s.Lines, locked[i].Count, preview, previewEach)).ToList();

        var pools = _main.Pools.ToDictionary(p => p.Id);

        // 훅은 복사본에만 끼운다 — 원본 레시피는 절대 건드리지 않는다. 일괄·단일 두 경로가
        // 모두 여기를 지나므로 훅은 자동으로 양쪽(미리보기 포함)에 적용된다.
        var hooks = EnabledHooks();
        var skipped = new List<string>();
        foreach (var recipe in recipes)
            foreach (var name in HookApplier.Apply(recipe, hooks, pools))
                if (!skipped.Contains(name)) skipped.Add(name);
        _skippedHooks = skipped;

        return new GenerationJob(recipes, pools, opts, lineCounts, _lastSeedUsed, lockKeys, locked, droppedLocks);
    }

    private List<GenerationHook> EnabledHooks() => _main.Hooks.Where(h => h.IsEnabled).OrderBy(h => h.Order).ToList();

    /// <summary>레시피 하나를 생성 카드와 똑같이(옵션·켜진 훅·품질 태그, 시드는 카드 칸 — 비었으면 무작위) <paramref name="count"/>줄 뽑는다.
    /// 라이브러리 "샘플 미리보기"용 — 예전엔 따로 정한 기본값(균등 추첨·자동 정렬 끔·훅 없음)이라 카드 미리보기·실제 파일과 다르게 나왔다.</summary>
    internal (IReadOnlyList<string> Lines, int Seed) SampleLines(Recipe recipe, int count)
    {
        var opts = BuildOptions();
        int seed = opts.Seed ?? Random.Shared.Next();
        var copy = Clone(recipe);
        var pools = _main.Pools.ToDictionary(p => p.Id);
        HookApplier.Apply(copy, EnabledHooks(), pools);
        var result = _generator.Generate(copy, pools, CopyWith(opts, count, seed), _main.Conflicts, _main.TagInfo);
        return (ApplyQualityTags(result).Lines, seed);
    }

    /// <summary>조합이 바닥나 요청보다 적게 뽑은 레시피 요약(상태 메시지 꼬리). 레시피별 경고 문구는 이걸로 대신한다.</summary>
    private static string ExhaustedNote(GenerationJob job, IReadOnlyList<RecipeOutput> outputs)
    {
        var capped = outputs.Select((o, i) => (o, i)).Where(x => x.o.Result.Exhausted)
            .Select(x => $"{x.o.Recipe.Name}({x.o.Result.Lines.Count}/{job.LineCounts[x.i]})").ToList();
        if (capped.Count == 0) return "";
        return $" ⚠ 조합이 바닥나 줄 수를 줄인 레시피 {capped.Count}개(중복 줄 안 넣음): "
            + string.Join(", ", capped.Take(5)) + (capped.Count > 5 ? $" 외 {capped.Count - 5}개" : "");
    }

    /// <summary>상태 메시지에 붙일 경고들 — 조합 바닥 경고는 <see cref="ExhaustedNote"/>가 레시피별로 모아 보여 주므로 뺀다.</summary>
    private static IEnumerable<string> OtherWarnings(GenerationResult result) =>
        result.Warnings.Where(w => !w.Contains("조합이 바닥나 ", StringComparison.Ordinal));

    /// <summary>잠근 줄을 어떻게 썼는지 상태 메시지 꼬리.</summary>
    private static string LockNote(GenerationJob job)
    {
        int kept = job.Locked.Sum(l => l.Count);
        var note = kept > 0 ? $" 📌 잠근 줄 {kept}개 유지" : "";
        if (job.DroppedLocks > 0) note += $" ⚠ 이번 대상이 아닌 레시피의 잠근 줄 {job.DroppedLocks}개는 목록에서 빠졌습니다";
        return note;
    }

    /// <summary>후보가 없어 건너뛴 훅이 있으면 알린다.</summary>
    private string SkippedHookNote() =>
        _skippedHooks.Count == 0 ? "" : $" ⚠ 후보가 없어 건너뛴 훅: {string.Join(", ", _skippedHooks)}";

    /// <summary>레시피 하나의 생성 결과(어느 레시피가 뽑았는지 함께). 레시피별 파일 분리
    /// 출력이 "어느 파일에 무엇을 쓸지" 알려면 합치기 전 단위가 남아 있어야 한다.</summary>
    private sealed record RecipeOutput(Recipe Recipe, string LockKey, GenerationResult Result, int LockedCount, long ElapsedMs = 0);

    /// <summary>생성 기록을 남긴다(백그라운드 — 실패해도 생성 결과에는 영향 없음, 상태 표시줄에만).</summary>
    private void SaveGenerationRecord(GenerationJob job, IReadOnlyList<RecipeOutput> outputs)
    {
        if (AppWriteGuard.Frozen) return;
        var record = new GenerationRecord
        {
            At = DateTime.Now,
            Seed = job.Seed,
            OutputPath = OutputPath,
            Mode = Mode.ToString(),
            OptionsSummary = $"중복 회피 {(job.Options.AvoidDuplicateLines ? "켬" : "끔")} · 모순 회피 {(job.Options.AvoidConflicts ? "켬" : "끔")}"
                             + $"(로컬 {(job.Options.LocalConflictFiltering ? "켬" : "끔")}) · {(job.Options.Sampling == SamplingMode.Weighted ? "빈도 가중" : "균등")}"
                             + (QualityTagsEnabled ? " · 품질 태그 붙임" : ""),
            Recipes = outputs.Select((o, i) => new GenerationRecordRecipe
            {
                Id = o.Recipe.Id, Name = o.Recipe.Name, LockKey = o.LockKey, Requested = job.LineCounts[i],
                Lines = o.Result.Lines.ToList(), LineKeys = o.Result.LineKeys.ToList(),
            }).ToList(),
        };
        _ = Task.Run(() =>
        {
            try { GenerationHistoryStore.Save(AppPaths.GenerationHistoryDir, record); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _main.Status = "⚠ 생성 기록 저장 실패 — " + ex.Message);
            }
        });
    }

    /// <summary>생성 기록 창(다시 띄우기·같은 시드로).</summary>
    [RelayCommand]
    private void ShowGenerationHistory() =>
        new Views.GenerationHistoryWindow(this) { Owner = System.Windows.Application.Current.MainWindow }.Show();

    /// <summary>기록을 결과 목록에 다시 띄운다(출처 정보는 없다 — 잠금·다시 뽑기는 된다).</summary>
    public void LoadGenerationRecord(GenerationRecord record)
    {
        var outputs = record.Recipes.Select(r => new RecipeOutput(
            new Recipe { Id = r.Id, Name = r.Name }, r.LockKey,
            new GenerationResult(r.Lines, Array.Empty<string>()) { LineKeys = r.LineKeys }, 0)).ToList();
        ShowResults(outputs);
        ConflictReport = "";
        _main.Status = $"{record.At:MM-dd HH:mm} 생성 기록({outputs.Sum(o => o.Result.Lines.Count)}줄, 시드 {record.Seed})을 목록에 띄웠습니다 — 줄 출처는 기록에 없습니다.";
    }

    public void UseSeed(int seed)
    {
        SeedText = seed.ToString();
        _main.Status = $"시드를 {seed}(으)로 맞췄습니다 — 같은 레시피·옵션이면 같은 줄이 나옵니다.";
    }

    /// <summary>마지막 일괄 생성에서 문제(조합 바닥·중복·모순 남음)가 있던 레시피 id — [⚠ 지난번 문제만].</summary>
    private List<string> _problemRecipeIds = new();
    /// <summary>"⚠ 지난번 문제만 (2)" 버튼 글자. 문제가 없었으면 빈 문자열(버튼 숨김).</summary>
    [ObservableProperty] private string _problemCountText = "";
    /// <summary>생성 뒤 둘째 줄: "⚠ 문제 3건(조합 바닥 2 · 모순 남은 줄 1) — 로그에 기록함". 문제가 없으면 빈 문자열.</summary>
    [ObservableProperty] private string _problemSummaryText = "";

    /// <summary>카드 '토큰 기준' 칸의 숫자. 비었거나 0 이하·숫자 아님이면 0(= 모델 기본값).</summary>
    private int ParsedTokenLimit() => int.TryParse(ProfileTokenLimitText.Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>지금 적용되는 토큰 규칙(카드 '토큰 기준'의 모델·숫자, 비면 모델 기본값).</summary>
    internal (Core.Tokenization.ITokenCounter Counter, int Limit) CurrentTokenRule()
    {
        // 카드의 '토큰 기준'(보이는 값) 그대로 — 예전엔 고른 저장 프로필에서만 읽어서, 새로 저장했거나 재시작한 뒤엔 IL로 돌아갔다.
        var model = ProfileModel;
        var limit = ParsedTokenLimit() is > 0 and var l ? l : Core.Tokenization.TokenCounters.DefaultLimit(model);
        return (Core.Tokenization.TokenCounters.For(model, AppPaths.TokenizersDir), limit);
    }

    /// <summary>레시피마다 문제만 잘라(<see cref="Core.Logging.GenerationProblems"/>) 카드 요약을 갱신하고, 일괄 "생성"일 때만 로그에 남긴다.
    /// 미리보기는 몇 줄뿐이라 판단 근거가 약하고 누를 때마다 같은 항목이 쌓여 로그에 남기지 않는다(카드에만 보인다).
    /// 생성도 같은 레시피 내용의 같은 문제는 하루 한 번만 남는다(<see cref="AppLog.Concept"/>).
    /// 문제 레시피 목록([⚠ 지난번 문제만])도 일괄 "생성" 때만 바꾼다.</summary>
    private void LogProblems(GenerationJob job, IReadOnlyList<RecipeOutput> outputs, bool isPreview)
    {
        var (counter, limit) = CurrentTokenRule();
        var now = DateTime.Now;
        var all = new List<(Core.Logging.LogEntry Entry, string RepeatKey)>();
        var problemIds = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < outputs.Count; i++)
        {
            var o = outputs[i];
            var input = new Core.Logging.GenerationProblemInput(
                o.Recipe, job.Pools, o.Result, job.LineCounts[i], isPreview, unchecked(job.Seed + i), counter, limit,
                ApplyQualityTags(o.Result).Lines, now);
            bool problem = false;
            foreach (var e in Core.Logging.GenerationProblems.From(input))
            {
                var kind = Core.Logging.GenerationProblems.KindOf(e) ?? e.Title;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                if (kind != Core.Logging.GenerationProblems.TooLong) problem = true;
                if (!isPreview) all.Add((e, Core.Logging.GenerationProblems.RepeatKey(input, e)));
            }
            if (problem) problemIds.Add(o.Recipe.Id);
        }
        int written = isPreview ? 0 : AppLog.Concept(all);

        int total = counts.Values.Sum();
        var parts = new[] { Core.Logging.GenerationProblems.Exhausted, Core.Logging.GenerationProblems.Duplicates,
                            Core.Logging.GenerationProblems.Conflicts, Core.Logging.GenerationProblems.TooLong }
            .Where(counts.ContainsKey).Select(k => $"{k} {counts[k]}");
        var where = isPreview ? "미리보기 — 로그엔 안 남김"
            : written == total ? "로그에 기록함"
            : written == 0 ? "같은 문제는 오늘 이미 로그에 있음"
            : $"새 문제 {written}건만 로그에 기록(나머지는 오늘 이미 있음)";
        ProblemSummaryText = total == 0 ? "" : $"⚠ 문제 {total}건({string.Join(" · ", parts)}) — {where}";

        if (!isPreview && IsMultiRecipeMode)
        {
            _problemRecipeIds = problemIds;
            ProblemCountText = problemIds.Count == 0 ? "" : $"⚠ 지난번 문제만 ({problemIds.Count})";
        }
    }

    [RelayCommand]
    private void OpenLog() => _main.OpenLogCommand.Execute(null);

    /// <summary>마지막 일괄 생성에서 문제가 있던 레시피만 체크한다 — 고친 뒤 그것만 다시 돌리는 용도.</summary>
    [RelayCommand]
    private void SelectProblemRecipes()
    {
        if (_problemRecipeIds.Count == 0) { _main.Status = "마지막 일괄 생성에서 문제가 있던 레시피가 없습니다."; return; }
        var ids = _problemRecipeIds.ToHashSet(StringComparer.Ordinal);
        foreach (var item in _allBatchItems) item.IsChecked = ids.Contains(item.Recipe.Id);
        IsMultiRecipeMode = true;
        RefreshBatchItems();
        _main.Status = $"문제가 있던 레시피 {ids.Count}개만 체크했습니다.";
    }

    /// <summary>레시피 하나에서 새로 뽑을 줄 수(잠근 줄만큼 덜 뽑는다).</summary>
    private static int FreshCount(GenerationJob job, int i) => Math.Max(0, job.LineCounts[i] - job.Locked[i].Count);

    /// <summary>레시피마다 lineCountEach줄씩 뽑는다. 레시피마다 시드를 base+순번으로 달리해
    /// 서로 다른 레시피가 완전히 같은 난수 시퀀스를 타지 않게 한다. UI를 전혀 건드리지
    /// 않으므로 백그라운드 스레드에서 그대로 돈다.
    /// <para>레시피끼리는 독립(각자 시드·스냅샷)이라 병렬로 돌린다 — 44팩 × 200줄이 순차 3.5초 → 0.4초(2026-11-12 실측).
    /// 시드는 순번으로 정해지고 결과는 순번 자리에 넣으므로 같은 시드면 순차 실행과 출력이 같다. 오류도
    /// 순차일 때와 같은 것(앞 순번)을 보고하려고 전부 모은 뒤 순번이 가장 앞선 것을 올린다.</para></summary>
    private List<RecipeOutput> RunJob(GenerationJob job, IProgress<int> progress, CancellationToken token)
    {
        int count = job.Recipes.Count;
        var results = new GenerationResult?[count];
        var elapsed = new long[count];
        var errors = new Exception?[count];
        var linesDone = new int[count];
        var conflicts = _main.Conflicts;
        var tagInfo = _main.TagInfo;

        Parallel.For(0, count, new ParallelOptions { CancellationToken = token }, i =>
        {
            var locked = job.Locked[i];
            var opts = CopyWith(job.Options, FreshCount(job, i), unchecked(job.Seed + i)) with
            {
                // 줄 출처 보기용. 기록만 하므로 같은 시드면 끈 때와 같은 줄이 나온다.
                CaptureOrigins = true,
                ReservedLineKeys = locked.Count > 0 ? locked.Select(l => l.LineKey).ToHashSet(StringComparer.Ordinal) : null,
            };
            var inner = new SyncProgress(n =>
            {
                Volatile.Write(ref linesDone[i], n);
                int sum = 0;
                for (int k = 0; k < linesDone.Length; k++) sum += Volatile.Read(ref linesDone[k]);
                progress.Report(sum);
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { results[i] = Compose(locked, _generator.Generate(job.Recipes[i], job.Pools, opts, conflicts, tagInfo, inner, token)); }
            catch (Exception ex) { errors[i] = ex; }
            elapsed[i] = sw.ElapsedMilliseconds;
        });

        token.ThrowIfCancellationRequested();
        for (int i = 0; i < count; i++)
        {
            switch (errors[i])
            {
                case null: continue;
                // 일괄 생성에선 레시피 하나의 검증 오류가 전체를 멈춘다 — 어느 레시피인지 붙여야 찾아 고칠 수 있다.
                case GenerationValidationException ex when count > 1:
                    throw new GenerationValidationException($"레시피 '{job.Recipes[i].Name}' — {ex.Message}");
                case var ex:
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex);
                    break;
            }
        }

        return Enumerable.Range(0, count)
            .Select(i => new RecipeOutput(job.Recipes[i], job.LockKeys[i], results[i]!, job.Locked[i].Count, elapsed[i])).ToList();
    }

    /// <summary>호출한 스레드에서 바로 콜백하는 진행률 보고기. <see cref="Progress{T}"/>는 만든 스레드의
    /// 컨텍스트로 넘겨 순서가 섞이므로, 병렬 합산은 이걸로 하고 UI로 넘기는 건 바깥 Progress 하나에 맡긴다.</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    /// <summary>레시피별 결과를 한 덩어리로 잇는다(레시피별로 묶여 순서대로 — 랜덤 셔플 아님).
    /// 모순 줄 인덱스는 이어붙인 뒤의 전체 위치로 보정하고, 경고에는 어느 레시피인지 붙인다.</summary>
    private static GenerationResult Merge(IReadOnlyList<RecipeOutput> outputs)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        var conflicts = new List<LineConflict>();

        foreach (var o in outputs)
        {
            int offset = lines.Count;
            foreach (var c in o.Result.Conflicts) conflicts.Add(c with { LineIndex = c.LineIndex + offset });
            lines.AddRange(o.Result.Lines);
            foreach (var w in o.Result.Warnings)
                warnings.Add(outputs.Count > 1 ? $"[{o.Recipe.Name}] {w}" : w);
        }

        return new GenerationResult(lines, warnings) { Conflicts = conflicts };
    }

    /// <summary>줄 수와 시드만 바꾼 사본. 백그라운드에서 뷰모델 속성을 다시 읽지 않으려고
    /// 스냅샷의 값만으로 만든다. 레코드 복사(with)라 GenerationOptions에 옵션이 늘어도 여기서 빠뜨릴 수 없다 —
    /// 예전엔 필드를 손으로 옮겨 적어서, 새 옵션을 여기 안 적으면 일괄 생성에서만 조용히 꺼졌다.</summary>
    private static GenerationOptions CopyWith(GenerationOptions o, int lineCount, int seed) =>
        o with { LineCount = lineCount, Seed = seed };

    /// <summary>스냅샷을 백그라운드에서 돌리고 진행률을 UI로 보고한다. 예전엔 생성이 UI
    /// 스레드에서 통째로 돌아, 레시피 수십 개 × 수백 줄이면 창이 몇 초씩 응답하지 않았다.</summary>
    private async Task<List<RecipeOutput>> RunInBackgroundAsync(GenerationJob job, CancellationToken token)
    {
        long total = Enumerable.Range(0, job.Recipes.Count).Sum(i => (long)FreshCount(job, i));
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


    /// <summary>미리보기 내용을 클립보드로. 읽기 전용 상자에서 드래그로 긁는 것보다 빠르다.</summary>
    [RelayCommand]
    private void CopyPreview()
    {
        if (string.IsNullOrEmpty(PreviewText)) { _main.Status = "복사할 미리보기 내용이 없습니다."; return; }
        try
        {
            System.Windows.Clipboard.SetText(PreviewText);
            _main.Status = $"미리보기 {PreviewText.Split('\n').Length}줄을 클립보드에 복사했습니다.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 다른 프로그램이 클립보드를 붙들고 있으면 실패한다 — 흔한 일이라 죽지 않게 한다.
            _main.Status = "클립보드를 다른 프로그램이 쓰고 있어 복사하지 못했습니다. 잠시 후 다시 시도하세요.";
        }
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
        // BuildJob과 같은 목록을 봐야 한다 — 표시용을 보면 "검색에 가려졌을 뿐 체크는 돼 있는"
        // 상태에서 생성이 막혀 버린다.
        if (!IsMultiRecipeMode || _allBatchItems.Any(b => b.IsChecked)) return false;
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
            var job = BuildJob(preview: true);
            var outputs = await RunInBackgroundAsync(job, token);
            var result = ApplyQualityTags(Merge(outputs));
            ShowResults(outputs);
            LogProblems(job, outputs, isPreview: true);
            ConflictReport = BuildConflictReport(result);
            ResultSummaryText = $"미리보기 {result.Lines.Count:N0}줄 · 시드 {_lastSeedUsed}" + LockNote(job);
            AutoOpenResultsIfWanted();
            var seedNote = $"(시드 {_lastSeedUsed})";
            var warns = OtherWarnings(result).ToList();
            _main.Status = (warns.Count > 0 ? string.Join(" / ", warns) : "미리보기 완료")
                + " " + seedNote + ExhaustedNote(job, outputs) + LockNote(job) + SkippedHookNote();
        }
        catch (OperationCanceledException) { _main.Status = "미리보기 취소됨"; }
        // 미리보기의 검증 오류는 상태 표시줄에만(로그엔 [생성] 때 남는다 — 누를 때마다 쌓이지 않게).
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
            var job = BuildJob(preview: false);
            var outputs = await RunInBackgroundAsync(job, token);
            var result = ApplyQualityTags(Merge(outputs));

            // 파일 쓰기도 백그라운드로. 취소된 뒤엔 아예 쓰지 않는다(파일은 그대로 남는다).
            var (path, mode, blank) = (OutputPath, Mode, InsertBlankLine);
            string where;
            if (SplitFilesPerRecipe && IsMultiRecipeMode)
            {
                var folder = Path.GetDirectoryName(path) ?? "";
                var names = WildcardWriter.ToFileNames(outputs.Select(o => (o.Recipe.Name, o.Recipe.Id)));
                var files = outputs.Select((o, i) =>
                    (Path: Path.Combine(folder, names[i] + ".txt"),
                     Lines: ApplyQualityTags(o.Result).Lines)).ToList();
                // "새 파일" 모드는 이미 있는 파일에서 예외로 멈춘다 — 쓰는 도중에 걸리면 앞쪽 파일만 써진 반쪽 결과가
                // 남으므로, 하나라도 겹치면 아무것도 쓰지 않고 알린다.
                if (mode == WriteMode.New)
                {
                    var clash = files.Where(f => File.Exists(f.Path)).Select(f => Path.GetFileName(f.Path)).ToList();
                    if (clash.Count > 0)
                    {
                        _main.Status = $"새 파일 모드인데 이미 있는 파일 {clash.Count}개 — 아무것도 쓰지 않았습니다: "
                            + string.Join(", ", clash.Take(5)) + (clash.Count > 5 ? " …" : "");
                        return;
                    }
                }
                await Task.Run(() =>
                {
                    foreach (var f in files)
                    {
                        token.ThrowIfCancellationRequested();
                        WildcardWriter.Write(f.Path, f.Lines, mode, blank);
                    }
                }, token);
                where = $"{files.Count}개 파일 → {folder}";
            }
            else
            {
                await Task.Run(() => WildcardWriter.Write(path, result.Lines, mode, blank), token);
                where = path;
            }

            _seedCarry.Written();   // 파일에 썼다 — 다음 생성은 새 시드
            _main.Settings.LastOutputDir = Path.GetDirectoryName(OutputPath) ?? "";
            SaveSettings();
            SaveGenerationRecord(job, outputs);
            _main.MarkRecipesUsed(IsMultiRecipeMode
                ? _allBatchItems.Where(b => b.IsChecked).Select(b => (string?)b.Recipe.Id)
                : new[] { _main.RecipeBuilder.LoadedRecipeId });
            // 쓴 내용을 목록에도 띄운다 — 예전엔 파일로만 나가서 무엇이 써졌는지 보려면 파일을 열어야 했다.
            ShowResults(outputs);
            LogProblems(job, outputs, isPreview: false);
            ConflictReport = BuildConflictReport(result);
            ResultSummaryText = (outputs.Count > 1 ? $"✓ {outputs.Count}개 레시피 " : "✓ ")
                + $"{result.Lines.Count:N0}줄 → {(SplitFilesPerRecipe && IsMultiRecipeMode ? where : Path.GetFileName(path))} · {ModeText(mode)} · 시드 {_lastSeedUsed}"
                + (_lastSeedCarried ? "(미리보기 시드 이어 씀)" : "")
                + LockNote(job);
            AutoOpenResultsIfWanted();
            var warns = OtherWarnings(result).ToList();
            _main.Status = $"{result.Lines.Count}줄 {Mode} 완료 → {where} (시드 {_lastSeedUsed}"
                + (_lastSeedCarried ? " · 미리보기 시드 이어 씀)" : ")")
                + (warns.Count > 0 ? " (" + string.Join(", ", warns) + ")" : "")
                + ExhaustedNote(job, outputs) + LockNote(job) + SkippedHookNote();
        }
        catch (OperationCanceledException) { _main.Status = "생성 취소됨 — 파일은 건드리지 않았습니다."; }
        catch (GenerationValidationException ex)
        {
            _main.Status = "검증 오류: " + ex.Message;
            AppLog.Program("생성 검증 오류", ex.Message);
        }
        catch (IOException ex)
        {
            _main.Status = "파일 오류: " + ex.Message;
            AppLog.Exception("파일 쓰기 실패 — " + OutputPath, ex);
        }
        finally { ProgressText = ""; }
    }
}
