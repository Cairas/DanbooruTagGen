using System.IO;
using System.Windows;

namespace DanbooruTagGen.App;

public partial class MainWindow : Window
{
    /// <summary>Ctrl+F: 지금 탭의 검색칸으로(일괄 생성 = 레시피 검색, 레시피 편집 = 태그 검색).</summary>
    private void FocusTagSearch()
    {
        var name = DataContext is ViewModels.MainViewModel { SelectedTabIndex: 0 } ? "BatchSearchBox" : "SearchBox";
        if (FindByName<System.Windows.Controls.TextBox>(this, name) is { } box) { box.Focus(); box.SelectAll(); }
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && t.Name == name) return t;
            if (FindByName<T>(child, name) is { } found) return found;
        }
        return null;
    }

    private void RestoreLayout()
    {
        if (DataContext is not ViewModels.MainViewModel { Settings: var s }) return;
        if (s.MainWindowWidth > 300 && s.MainWindowHeight > 200) { Width = s.MainWindowWidth; Height = s.MainWindowHeight; }
        // 모니터 구성이 바뀌어 화면 밖이 되면 위치는 버린다(기본 위치).
        if (!double.IsNaN(s.MainWindowLeft) && !double.IsNaN(s.MainWindowTop)
            && s.MainWindowLeft >= SystemParameters.VirtualScreenLeft - 50
            && s.MainWindowTop >= SystemParameters.VirtualScreenTop - 50
            && s.MainWindowLeft + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && s.MainWindowTop + 50 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.MainWindowLeft;
            Top = s.MainWindowTop;
        }
        if (s.MainWindowMaximized) WindowState = WindowState.Maximized;
        if (s.MainLeftColumnWidth > 100) LeftColumn.Width = new GridLength(s.MainLeftColumnWidth);
        if (s.MainRightColumnWidth > 100) RightColumn.Width = new GridLength(s.MainRightColumnWidth);
    }

    private void RememberLayout()
    {
        if (DataContext is not ViewModels.MainViewModel { Settings: var s }) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            (s.MainWindowLeft, s.MainWindowTop, s.MainWindowWidth, s.MainWindowHeight) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
        s.MainWindowMaximized = WindowState == WindowState.Maximized;
        // 편집 탭을 한 번도 안 열었으면 왼쪽 칸 너비가 0이다 — 저장해 둔 값을 덮어쓰지 않는다.
        if (LeftColumn.ActualWidth > 0) s.MainLeftColumnWidth = LeftColumn.ActualWidth;
        if (RightColumn.ActualWidth > 0) s.MainRightColumnWidth = RightColumn.ActualWidth;
    }

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        // DataContext는 App이 Show 전에 넣는다 — 그때 저장해 둔 창 위치·칸 너비를 되살리면 화면이 튀지 않는다.
        DataContextChanged += (_, _) => RestoreLayout();
        InputBindings.Add(new System.Windows.Input.KeyBinding(
            new CommunityToolkit.Mvvm.Input.RelayCommand(FocusTagSearch), System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.Control));
        // 구빌드로 며칠 테스트한 일이 있었다 — 지금 돌고 있는 빌드가 언제 것인지 항상 보이게 한다.
        Title = $"DanbooruTagGen · 빌드 {Services.AppPaths.BuildTime:MM-dd HH:mm}"
                + (Services.AppPaths.DevMode ? " · 🛠 개발 모드(저장소 data 직접)" : "");
    }

    /// <summary>창을 닫을 때 현재 레시피와 생성 탭 설정을 자동 저장한다 — 저장 버튼 누르는 걸
    /// 잊어도 작업이 사라지지 않게 하는 안전망(Pool은 애초에 명시적 저장만 지원해 별도).
    /// RecipeBuilder/Generation은 태그 CSV 로드가 끝난 뒤에야 생성되므로, 초기화 도중 닫으면
    /// 아직 null일 수 있어 확인한다. 저장 실패로 종료 자체가 막히면 안 되므로 IOException만 삼킨다.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => SaveOnClose();

    /// <summary>닫을 때의 자동 저장. 백업 복원은 되돌리기 전에 이걸 먼저 불러 "지금 상태"를 디스크에 맞춘 뒤 스냅샷을 뜬다.
    /// 복원 후 재시작 중(쓰기 동결)이면 아무것도 안 한다 — 되돌린 파일을 메모리의 옛 상태로 덮어쓰면 안 된다.</summary>
    public void SaveOnClose()
    {
        if (Services.AppWriteGuard.Frozen) return;
        RememberLayout();
        if (DataContext is ViewModels.MainViewModel { RecipeBuilder: not null } vm)
        {
            try { vm.RecipeBuilder.SaveRecipeCommand.Execute(null); }
            catch (IOException) { /* 종료 중 저장 실패는 무시 — 창 닫힘을 막지 않는다 */ }
        }
        if (DataContext is ViewModels.MainViewModel { Generation: not null } vm2)
        {
            try { vm2.Generation.SaveSettings(); }
            catch (IOException) { /* 종료 중 저장 실패는 무시 — 창 닫힘을 막지 않는다 */ }
        }
    }
}
