using System.Linq;
using System.Windows;
using System.Windows.Input;
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

    // ── 슬롯 순서 드래그 앤 드롭 ──────────────────────────────────────────
    // 손잡이(⠿)를 누른 지점만 기억해 뒀다가, 그 상태로 시스템 드래그 임계값(SystemParameters.
    // Minimum*DragDistance)을 넘게 움직이면 그때 딱 한 번 DragDrop.DoDragDrop을 시작한다.
    // 누른 동안 커서가 손잡이 밖으로 나가도 계속 MouseMove를 받아야 하므로 캡처를 걸고,
    // 그 캡처를 Expander 헤더의 ToggleButton에게 뺏기지 않도록 아래에서 Handled로 막는다.
    // 그 뒤로는 WPF가 드래그를 통째로 가져가므로 MouseMove가 더 안 불린다 — 즉 목록은
    // 드래그 도중에는 전혀 갱신되지 않고, Drop 이벤트 한 번에만 뷰모델에 반영된다.
    private Point _dragStart;
    private bool _dragArmed;

    // e.Handled = true가 이 기능의 핵심이다. Expander의 기본 템플릿은 헤더를 ToggleButton으로
    // 감싸는데, ButtonBase.OnMouseLeftButtonDown이 버블링 단계에서 자기 자신에게 CaptureMouse()를
    // 건다. 즉 여기서 손잡이에 캡처를 걸어도 1ms 뒤 ToggleButton이 캡처를 통째로 가져가 버리고,
    // 그때부터 모든 MouseMove는 ToggleButton의 이벤트 경로로만 흘러 손잡이에는 영영 안 온다
    // — PreviewMouseMove가 안 불리니 임계값 판정도, DoDragDrop 호출도 일어나지 않았다.
    // (실측 로그: CaptureMouse()=True → Mouse.Captured=TextBlock → 1ms 뒤 Mouse.Captured=ToggleButton,
    //  이후 손잡이의 PreviewMouseMove 0회.)
    // 터널링 단계에서 Handled로 막으면 대응하는 버블링 MouseLeftButtonDown이 아예 발생하지 않아
    // ToggleButton이 캡처를 뺏지 못한다. 부수 효과로 손잡이를 클릭해도 Expander가 펼쳐지지 않는데,
    // 손잡이는 드래그 전용이라 오히려 이쪽이 맞는 동작이다(라벨·화살표 등 헤더의 나머지 부분은
    // 그대로 펼치기/접기로 동작한다).
    private void SlotDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragStart = e.GetPosition(null);
        _dragArmed = true;
        (sender as UIElement)?.CaptureMouse();
    }

    private void SlotDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = false;
        (sender as UIElement)?.ReleaseMouseCapture();
    }

    private void SlotDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragArmed = false;
        if (sender is not FrameworkElement handle || handle.DataContext is not SlotPreviewViewModel item) return;
        handle.ReleaseMouseCapture(); // DoDragDrop이 자기 방식으로 캡처를 관리하니 먼저 놓아준다.
        DragDrop.DoDragDrop(handle, item, DragDropEffects.Move);
    }

    /// <summary>드롭된 순간에만 한 번 호출 — 놓인 슬롯(sender의 DataContext) 위치로
    /// 끌어온 슬롯(e.Data)을 옮긴다.</summary>
    private void SlotExpander_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not RecipeLibraryViewModel vm) return;
        if (sender is not FrameworkElement target || target.DataContext is not SlotPreviewViewModel dropTarget) return;
        if (e.Data.GetData(typeof(SlotPreviewViewModel)) is not SlotPreviewViewModel dragged) return;
        vm.ReorderSlot(dragged, dropTarget);
        e.Handled = true;
    }
}
