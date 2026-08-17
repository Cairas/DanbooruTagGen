using System.Linq;
using DanbooruTagGen.App.ViewModels;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.App.Views;

public partial class RecipeLibraryWindow : System.Windows.Window
{
    public RecipeLibraryWindow() => InitializeComponent();

    /// <summary>SelectionMode=Extended라 SelectedItem(단일 바인딩)만으로는 Ctrl/Shift로 고른
    /// 전체 선택을 알 수 없다 — ListBox.SelectedItems를 뷰모델에 직접 옮겨준다.</summary>
    private void RecipeListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is RecipeLibraryViewModel vm)
            vm.SetMultiSelection(RecipeListBox.SelectedItems.Cast<Recipe>());
    }
}
