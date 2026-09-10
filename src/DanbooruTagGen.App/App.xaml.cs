using System.Windows;
using DanbooruTagGen.App.ViewModels;

namespace DanbooruTagGen.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 실행 중 튀어나온 예외로 창이 그냥 사라지지 않게 한다. 이 앱은 스크립트가 data/presets를
        // 고치는 동안 계속 띄워 두고 쓰는 물건이라, 파일 잠금 같은 일시적 실패로 죽으면 편집 중인
        // 레시피가 통째로 날아간다. 무엇 때문에 실패했는지 보여 주고 계속 살려 둔다.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"처리하지 못한 오류가 발생했습니다.\n\n{args.Exception.Message}",
                "DanbooruTagGen", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        try
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            window.Show();
            await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            // 시작 자체가 실패한 경우(사용자 데이터 파일을 못 읽는 등)는 반쪽 상태로 계속
            // 가면 안 된다 — 빈 목록을 들고 돌다가 자동 저장이 원본을 덮어쓰면 레시피가
            // 전부 사라진다. 이유를 알리고 그대로 종료한다.
            MessageBox.Show(
                $"시작하지 못했습니다.\n\n{ex.Message}\n\n" +
                "다른 프로그램이 데이터 파일을 열고 있는지 확인한 뒤 다시 실행하세요.",
                "DanbooruTagGen", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
