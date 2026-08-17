using System.Windows.Controls;

namespace DanbooruTagGen.App.Views;

public partial class TagSearchView : UserControl
{
    public TagSearchView() => InitializeComponent();

    // 한글 IME 조합 중에도 즉시 검색되도록, 바인딩 대신 TextChanged로 Query를 직접 갱신.
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ViewModels.TagSearchViewModel vm && sender is TextBox tb)
            vm.Query = tb.Text;
    }

    private void Results_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.TagSearchViewModel vm && vm.AddSelectedCommand.CanExecute(null))
            vm.AddSelectedCommand.Execute(null);
    }
}
