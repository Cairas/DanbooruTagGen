namespace DanbooruTagGen.App.Views;

public partial class ConceptWizardWindow : System.Windows.Window
{
    public ConceptWizardWindow() => InitializeComponent();

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // 레시피 생성이 끝나면 VM이 닫아 달라고 신호를 보낸다.
        if (DataContext is ViewModels.ConceptWizardViewModel vm)
            vm.RequestClose += Close;
    }
}
