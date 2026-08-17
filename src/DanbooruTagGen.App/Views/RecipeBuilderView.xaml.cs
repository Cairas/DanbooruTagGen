namespace DanbooruTagGen.App.Views;

public partial class RecipeBuilderView : System.Windows.Controls.UserControl
{
    public RecipeBuilderView() => InitializeComponent();

    private void SaveSlotToPool_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 컨텍스트 메뉴의 DataContext는 우클릭한 슬롯(ListBoxItem의 DataContext).
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is DanbooruTagGen.Core.Models.Slot slot
            && DataContext is ViewModels.RecipeBuilderViewModel vm)
        {
            vm.SaveSlotToPool(slot);
        }
    }

    private void TagChip_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not string tag) return;
        // 시각트리 상위에서 슬롯(고정/랜덤) DataContext를 찾는다
        var dep = (System.Windows.DependencyObject)fe;
        while (dep != null)
        {
            if (dep is System.Windows.FrameworkElement el && el.DataContext is DanbooruTagGen.Core.Models.Slot slot)
            {
                if (DataContext is ViewModels.RecipeBuilderViewModel vm) vm.RemoveTagFromSlot(slot, tag);
                return;
            }
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
    }

    /// <summary>랜덤 슬롯의 풀 콤보에서 저장된 풀을 고르면 PoolId는 바인딩이 이미 반영했고,
    /// 여기서는 모순 경고 재계산과 상태 메시지만 처리한다.</summary>
    private void PoolCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe
            || fe.DataContext is not DanbooruTagGen.Core.Models.RandomPoolSlot slot
            || DataContext is not ViewModels.RecipeBuilderViewModel vm) return;

        vm.RefreshConflicts();
        vm.NotifyPoolAttached(slot);
    }

    private void ClearPool_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is DanbooruTagGen.Core.Models.RandomPoolSlot slot
            && DataContext is ViewModels.RecipeBuilderViewModel vm)
        {
            slot.PoolId = "";
            vm.RefreshConflicts();
        }
    }

    /// <summary>풀 체이닝 콤보에서 풀을 고르면 ExtraPoolIds에 추가하고, 다시 고를 수 있게
    /// 콤보 선택을 즉시 초기화한다(선택 상태로 남으면 "고른 것"처럼 보여 혼동됨).</summary>
    private void AddExtraPool_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo
            || combo.SelectedValue is not string poolId || string.IsNullOrEmpty(poolId)) return;
        if (combo.DataContext is not DanbooruTagGen.Core.Models.RandomPoolSlot slot
            || DataContext is not ViewModels.RecipeBuilderViewModel vm) return;

        vm.AddExtraPool(slot, poolId);
        combo.SelectedIndex = -1;
    }

    /// <summary>체이닝된 풀 칩을 클릭하면 그 풀만 체이닝 해제(원본 풀 자체는 그대로).</summary>
    private void ExtraPoolChip_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.Tag is not string poolId) return;
        var dep = (System.Windows.DependencyObject)fe;
        while (dep != null)
        {
            if (dep is System.Windows.FrameworkElement el && el.DataContext is DanbooruTagGen.Core.Models.RandomPoolSlot slot)
            {
                if (DataContext is ViewModels.RecipeBuilderViewModel vm) vm.RemoveExtraPool(slot, poolId);
                return;
            }
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
    }

    /// <summary>슬롯 라벨을 더블클릭하면 인라인 편집 모드로 전환한다(예전엔 풀 라이브러리에
    /// 저장할 때만 이름을 고칠 수 있었음). 편집용 텍스트박스는 같은 이름 옆 형제 요소.</summary>
    private void SlotLabel_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not DanbooruTagGen.Core.Models.Slot slot) return;
        BeginEditingLabel(slot, fe);
    }

    /// <summary>선택된 슬롯에서 F2를 누르면 편집 모드로 전환한다. ItemContainerGenerator로
    /// 선택된 슬롯의 컨테이너를 찾은 뒤, 그 안에서 편집용 텍스트박스(LabelEditor)를 내려가며 찾는다.</summary>
    private void SlotsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.F2) return;
        if (sender is not System.Windows.Controls.ListBox listBox
            || DataContext is not ViewModels.RecipeBuilderViewModel vm
            || vm.SelectedSlot is not { } slot) return;

        var container = listBox.ItemContainerGenerator.ContainerFromItem(slot) as System.Windows.DependencyObject;
        if (container == null) return;
        slot.IsEditingLabel = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindDescendantByName<System.Windows.Controls.TextBox>(container, "LabelEditor") is { } tb)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
        e.Handled = true;
    }

    private void BeginEditingLabel(DanbooruTagGen.Core.Models.Slot slot, System.Windows.DependencyObject anchor)
    {
        slot.IsEditingLabel = true;
        // Visibility가 Collapsed→Visible로 바뀐 뒤라야 포커스가 먹으므로 레이아웃 패스 이후로 미룬다.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindSiblingTextBox(anchor) is { } tb)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static System.Windows.Controls.TextBox? FindSiblingTextBox(System.Windows.DependencyObject start)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(start);
        if (parent is not System.Windows.Controls.Panel panel) return null;
        foreach (var child in panel.Children)
            if (child is System.Windows.Controls.TextBox tb) return tb;
        return null;
    }

    private static T? FindDescendantByName<T>(System.Windows.DependencyObject root, string name) where T : System.Windows.FrameworkElement
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name) return match;
            if (FindDescendantByName<T>(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>편집용 텍스트박스가 포커스를 잃으면(Tab, 다른 곳 클릭 등) 편집 모드를 끝낸다.</summary>
    private void SlotLabelEditor_LostFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is DanbooruTagGen.Core.Models.Slot slot)
            slot.IsEditingLabel = false;
    }

    /// <summary>Enter/Esc로 편집을 끝낸다. 둘 다 그 시점까지 입력한 값을 그대로 두고 빠져나간다
    /// (Esc가 원래 값으로 되돌리진 않음 — 라벨은 출력에 안 들어가는 가벼운 값이라 굳이 취소 스냅샷을 안 둠).</summary>
    private void SlotLabelEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not DanbooruTagGen.Core.Models.Slot slot) return;
        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape)
        {
            slot.IsEditingLabel = false;
            e.Handled = true;
        }
    }

    /// <summary>슬롯 켜기/끄기 체크박스: IsEnabled는 바인딩이 이미 반영했고, 여기서는
    /// 꺼진 슬롯의 태그가 모순 경고에 계속 잡히지 않도록 재계산만 한다.</summary>
    private void SlotEnabled_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RecipeBuilderViewModel vm) vm.RefreshConflicts();
    }

    /// <summary>"+그룹" 버튼은 대안 슬롯 자체의 템플릿 안에 있어 DataContext가 곧 그 슬롯이다
    /// (SelectedSlot을 쓰면 리스트에 대안 슬롯이 여러 개일 때 엉뚱한 슬롯에 추가될 수 있음).</summary>
    private void AddAlternativeGroup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is DanbooruTagGen.Core.Models.AlternativeSlot slot
            && DataContext is ViewModels.RecipeBuilderViewModel vm)
        {
            vm.AddAlternativeGroup(slot);
        }
    }

    /// <summary>"그룹 삭제" 버튼의 DataContext는 그 그룹 자신 — 상위로 올라가 이 그룹을 담고
    /// 있는 대안 슬롯을 찾아서 지운다.</summary>
    private void RemoveAlternativeGroup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe
            || fe.DataContext is not DanbooruTagGen.Core.Models.AlternativeGroup group) return;
        var dep = (System.Windows.DependencyObject)fe;
        while (dep != null)
        {
            if (dep is System.Windows.FrameworkElement el && el.DataContext is DanbooruTagGen.Core.Models.AlternativeSlot slot)
            {
                if (DataContext is ViewModels.RecipeBuilderViewModel vm) vm.RemoveAlternativeGroup(slot, group);
                return;
            }
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
    }

    /// <summary>대안 그룹 안의 태그 칩 클릭 시 그 그룹의 Tags에서만 지운다(공용 TagChip과 달리
    /// '슬롯' 단위가 아니라 '그룹' 단위라 전용 핸들러가 필요).</summary>
    private void AlternativeTagChip_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not string tag) return;
        var dep = (System.Windows.DependencyObject)fe;
        while (dep != null)
        {
            if (dep is System.Windows.FrameworkElement el && el.DataContext is DanbooruTagGen.Core.Models.AlternativeGroup group)
            {
                group.Tags.Remove(tag);
                if (DataContext is ViewModels.RecipeBuilderViewModel vm) vm.RefreshConflicts();
                return;
            }
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
    }

    private void AddAlternativeGroupTag_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is DanbooruTagGen.Core.Models.AlternativeGroup group
            && DataContext is ViewModels.RecipeBuilderViewModel vm)
        {
            vm.AddTagsToGroup(group, group.PendingTagInput);
            group.PendingTagInput = "";
        }
    }

    private void AlternativeGroupInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is DanbooruTagGen.Core.Models.AlternativeGroup group
            && DataContext is ViewModels.RecipeBuilderViewModel vm)
        {
            vm.AddTagsToGroup(group, group.PendingTagInput);
            group.PendingTagInput = "";
            e.Handled = true;
        }
    }
}
