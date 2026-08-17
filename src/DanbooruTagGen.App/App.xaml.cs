using System.Windows;
using DanbooruTagGen.App.ViewModels;

namespace DanbooruTagGen.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.InitializeAsync();
    }
}

