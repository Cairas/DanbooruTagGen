using System.IO;
using System.Windows;

namespace DanbooruTagGen.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    /// <summary>창을 닫을 때 현재 레시피와 생성 탭 설정을 자동 저장한다 — 저장 버튼 누르는 걸
    /// 잊어도 작업이 사라지지 않게 하는 안전망(Pool은 애초에 명시적 저장만 지원해 별도).
    /// RecipeBuilder/Generation은 태그 CSV 로드가 끝난 뒤에야 생성되므로, 초기화 도중 닫으면
    /// 아직 null일 수 있어 확인한다. 저장 실패로 종료 자체가 막히면 안 되므로 IOException만 삼킨다.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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
