using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace TodoApp;

public sealed partial class MainPage : Page
{
    private const string IssuesFileName = "issues.json";
    private const string LegacyTodosFileName = "todos.json";

    private readonly ObservableCollection<IssueItem> _issues = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private IssueItem? _selectedIssue;
    private string _navScope = "All";
    private bool _isLoading;
    private bool _isRefreshingSelection;
    private bool _isSyncingNativeControls;
    private bool _isSyncingNavigation;
    private bool _hasAnimatedViewSurface;
    private AppVisualTheme _visualTheme = AppVisualTheme.Liquid;
    private string _searchQuery = string.Empty;
    private string _statusFilter = "All";
    private string _priorityFilter = "All";

    public ObservableCollection<IssueItem> VisibleIssues { get; } = new();
    public ObservableCollection<IssueItem> BacklogIssues { get; } = new();
    public ObservableCollection<IssueItem> TodoIssues { get; } = new();
    public ObservableCollection<IssueItem> InProgressIssues { get; } = new();
    public ObservableCollection<IssueItem> ReviewIssues { get; } = new();
    public ObservableCollection<IssueItem> DoneIssues { get; } = new();

    public MainPage()
    {
        _visualTheme = VisualThemeManager.LoadSavedTheme();

        InitializeComponent();

        VisualThemeManager.ThemeApplied += VisualThemeManager_ThemeApplied;
        Unloaded += Page_Unloaded;
        UpdateThemeDropDownSelection();
        ShellNavigation.SelectedItem = AllIssuesNavItem;
        SelectDropDownByTag(StatusFilterDropDownButton, "All");
        SelectDropDownByTag(PriorityFilterDropDownButton, "All");
        SelectComboBoxByTag(FluentStatusFilterComboBox, _statusFilter);
        SelectComboBoxByTag(FluentPriorityFilterComboBox, _priorityFilter);
        UpdateThemeSurfaceMode();
        _ = LoadIssuesAsync();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_visualTheme != AppVisualTheme.Liquid)
        {
            DispatcherQueue.TryEnqueue(() => ApplyVisualTheme(_visualTheme, save: false));
        }

        UpdateThemeSurfaceMode();

        if (!UsesNativeFluentSurface && CustomThemeRoot.Resources["LiquidGlassDriftStoryboard"] is Storyboard storyboard)
        {
            storyboard.Begin();
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        VisualThemeManager.ThemeApplied -= VisualThemeManager_ThemeApplied;
        Unloaded -= Page_Unloaded;
    }

    private void VisualThemeManager_ThemeApplied(AppVisualTheme theme)
    {
        UpdateThemeSurfaceMode();
    }

    private void BackdropDistortion_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            LiquidBackdropEffect.Attach(element, element.Tag as string);
        }
    }

    private void BackdropDistortion_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            LiquidBackdropEffect.Detach(element);
        }
    }

    private void GlassSurface_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateGlassSurface(sender, translateY: 0, opacity: 0.99, scale: 1.0, skewX: 0, durationMilliseconds: 180);
    }

    private void GlassSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
    }

    private void GlassSurface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateGlassSurface(sender, translateY: 0, opacity: 1, scale: 1, skewX: 0, durationMilliseconds: 210);
    }

    private void GlassSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AnimateGlassSurface(sender, translateY: 0, opacity: 0.94, scale: 0.998, skewX: 0, durationMilliseconds: 95);
    }

    private void GlassSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AnimateGlassSurface(sender, translateY: 0, opacity: 0.99, scale: 1.0, skewX: 0, durationMilliseconds: 135);
    }

    private static void AnimateGlassSurface(object sender, double translateY, double opacity, double scale, double skewX, double durationMilliseconds)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);

        if (element.RenderTransform is not CompositeTransform)
        {
            element.RenderTransform = new CompositeTransform();
        }

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var yAnimation = new DoubleAnimation
        {
            To = translateY,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = easing
        };
        Storyboard.SetTarget(yAnimation, element);
        Storyboard.SetTargetProperty(yAnimation, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");

        var opacityAnimation = new DoubleAnimation
        {
            To = opacity,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacityAnimation, element);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var scaleXAnimation = new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = easing
        };
        Storyboard.SetTarget(scaleXAnimation, element);
        Storyboard.SetTargetProperty(scaleXAnimation, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");

        var scaleYAnimation = new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = easing
        };
        Storyboard.SetTarget(scaleYAnimation, element);
        Storyboard.SetTargetProperty(scaleYAnimation, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");

        var skewXAnimation = new DoubleAnimation
        {
            To = skewX,
            Duration = duration,
            EnableDependentAnimation = true,
            EasingFunction = easing
        };
        Storyboard.SetTarget(skewXAnimation, element);
        Storyboard.SetTargetProperty(skewXAnimation, "(UIElement.RenderTransform).(CompositeTransform.SkewX)");

        storyboard.Children.Add(yAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Children.Add(scaleXAnimation);
        storyboard.Children.Add(scaleYAnimation);
        storyboard.Children.Add(skewXAnimation);
        storyboard.Begin();
    }

    private async void NewIssue_Click(object sender, RoutedEventArgs e)
    {
        await AddIssueAsync("새 이슈", selectAfterCreate: true);
    }

    private async void AddQuickIssue_Click(object sender, RoutedEventArgs e)
    {
        await AddIssueAsync(QuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private async void QuickIssueTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await AddIssueAsync(QuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _searchQuery = SearchTextBox.Text.Trim();
        SyncSearchTextBoxes(SearchTextBox);
        RefreshViews();
    }

    private void FluentSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _searchQuery = FluentSearchTextBox.Text.Trim();
        SyncSearchTextBoxes(FluentSearchTextBox);
        RefreshViews();
    }

    private async void FluentNewIssue_Click(object sender, RoutedEventArgs e)
    {
        await AddIssueAsync("새 이슈", selectAfterCreate: true);
    }

    private async void FluentAddQuickIssue_Click(object sender, RoutedEventArgs e)
    {
        await AddIssueAsync(FluentQuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private async void FluentQuickIssueTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await AddIssueAsync(FluentQuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private void FluentStatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _statusFilter = ReadSelectedComboBoxTag(FluentStatusFilterComboBox, "All");
        SelectDropDownByTag(StatusFilterDropDownButton, _statusFilter);
        RefreshViews();
    }

    private void FluentPriorityFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _priorityFilter = ReadSelectedComboBoxTag(FluentPriorityFilterComboBox, "All");
        SelectDropDownByTag(PriorityFilterDropDownButton, _priorityFilter);
        RefreshViews();
    }

    private void FluentThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        if (TryReadSelectedComboBoxTag(FluentThemeComboBox, out var tag)
            && VisualThemeManager.TryParseTag(tag, out var theme)
            && theme != _visualTheme)
        {
            ApplyVisualTheme(theme, save: true);
        }
    }

    private void ThemeDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(ThemeDropDownButton, ThemeOverlay);
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && VisualThemeManager.TryParseTag(tag, out var theme))
        {
            ApplyVisualTheme(theme, save: true);
        }

        CloseFilterOverlay();
    }

    private void ApplyVisualTheme(AppVisualTheme theme, bool save)
    {
        _visualTheme = theme;
        VisualThemeManager.Apply(theme);

        if (save)
        {
            VisualThemeManager.SaveTheme(theme);
        }

        UpdateThemeDropDownSelection();
        UpdateThemeSurfaceMode();
    }

    private bool UsesNativeFluentSurface => VisualThemeManager.CurrentDefinition.WindowTreatment == AppWindowTreatment.Opaque;

    private void UpdateThemeSurfaceMode()
    {
        var useNativeFluent = UsesNativeFluentSurface;
        CustomThemeRoot.Visibility = useNativeFluent ? Visibility.Collapsed : Visibility.Visible;
        FluentNativeRoot.Visibility = useNativeFluent ? Visibility.Visible : Visibility.Collapsed;

        if (useNativeFluent)
        {
            CloseFilterOverlay();
        }
        else if (CustomThemeRoot.Resources["LiquidGlassDriftStoryboard"] is Storyboard storyboard)
        {
            storyboard.Begin();
        }

        SyncNavigationSelection();
        SyncNativeFilterControls();
        SyncSearchTextBoxes();

        if (_selectedIssue is null)
        {
            UpdateDetailVisibility(hasSelection: false);
        }
        else
        {
            LoadIssueIntoEditor(_selectedIssue);
        }
    }

    private void UpdateThemeDropDownSelection()
    {
        var tag = VisualThemeManager.ToTag(_visualTheme);
        ThemeDropDownButton.Tag = tag;
        ToolTipService.SetToolTip(ThemeDropDownButton, $"테마: {VisualThemeManager.GetDisplayName(_visualTheme)}");
        RefreshDropDownMenuSelection(ThemeDropDownButton);
        SelectComboBoxByTag(FluentThemeComboBox, tag);
    }

    private void StatusFilterDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(StatusFilterDropDownButton, StatusFilterOverlay);
    }

    private void PriorityFilterDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(PriorityFilterDropDownButton, PriorityFilterOverlay);
    }

    private void DetailStatusDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(DetailStatusDropDownButton, DetailStatusOverlay);
    }

    private void DetailPriorityDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(DetailPriorityDropDownButton, DetailPriorityOverlay);
    }

    private void DetailProjectDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(DetailProjectDropDownButton, DetailProjectOverlay);
    }

    private void DetailAssigneeDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFilterOverlay(DetailAssigneeDropDownButton, DetailAssigneeOverlay);
    }

    private void StatusFilterMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(StatusFilterDropDownButton, sender);
        _statusFilter = ReadSelectedTag(StatusFilterDropDownButton, "All");
        SelectComboBoxByTag(FluentStatusFilterComboBox, _statusFilter);
        CloseFilterOverlay();
        RefreshViews();
    }

    private void PriorityFilterMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(PriorityFilterDropDownButton, sender);
        _priorityFilter = ReadSelectedTag(PriorityFilterDropDownButton, "All");
        SelectComboBoxByTag(FluentPriorityFilterComboBox, _priorityFilter);
        CloseFilterOverlay();
        RefreshViews();
    }

    private void FilterOverlayHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CloseFilterOverlay();
    }

    private void FilterOverlayPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void ToggleFilterOverlay(DropDownButton button, FrameworkElement overlay)
    {
        var shouldOpen = FilterOverlayHost.Visibility != Visibility.Visible || overlay.Visibility != Visibility.Visible;
        CloseFilterOverlay();

        if (!shouldOpen)
        {
            return;
        }

        FilterOverlayHost.Visibility = Visibility.Visible;
        FilterOverlayHost.UpdateLayout();
        FilterOverlayCanvas.UpdateLayout();
        overlay.UpdateLayout();
        PositionFilterOverlay(button, overlay);
        overlay.Visibility = Visibility.Visible;
        RefreshDropDownMenuSelection(button);
    }

    private void PositionFilterOverlay(FrameworkElement anchor, FrameworkElement overlay)
    {
        var transform = anchor.TransformToVisual(FilterOverlayCanvas);
        var point = transform.TransformPoint(new Point(0, anchor.ActualHeight + 7));
        var overlayWidth = overlay.ActualWidth > 0 ? overlay.ActualWidth : overlay.Width;
        if (double.IsNaN(overlayWidth) || overlayWidth <= 0)
        {
            overlayWidth = anchor.ActualWidth;
        }

        var maxLeft = Math.Max(0, FilterOverlayCanvas.ActualWidth - overlayWidth - 8);
        var left = Math.Min(Math.Max(8, point.X), maxLeft);
        Canvas.SetLeft(overlay, Math.Round(left));
        Canvas.SetTop(overlay, Math.Round(point.Y));
    }

    private void CloseFilterOverlay()
    {
        ThemeOverlay.Visibility = Visibility.Collapsed;
        StatusFilterOverlay.Visibility = Visibility.Collapsed;
        PriorityFilterOverlay.Visibility = Visibility.Collapsed;
        DetailStatusOverlay.Visibility = Visibility.Collapsed;
        DetailPriorityOverlay.Visibility = Visibility.Collapsed;
        DetailProjectOverlay.Visibility = Visibility.Collapsed;
        DetailAssigneeOverlay.Visibility = Visibility.Collapsed;
        FilterOverlayHost.Visibility = Visibility.Collapsed;
    }

    private void DetailStatusMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(DetailStatusDropDownButton, sender);
        CloseFilterOverlay();
    }

    private void DetailPriorityMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(DetailPriorityDropDownButton, sender);
        CloseFilterOverlay();
    }

    private void DetailProjectMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(DetailProjectDropDownButton, sender);
        CloseFilterOverlay();
    }

    private void DetailAssigneeMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(DetailAssigneeDropDownButton, sender);
        CloseFilterOverlay();
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSyncingNavigation)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            _navScope = tag;
            SyncNavigationSelection();
            RefreshViews();
        }
    }

    private void FluentNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSyncingNavigation)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            _navScope = tag;
            SyncNavigationSelection();
            RefreshViews();
        }
    }

    private void ListMode_Click(object sender, RoutedEventArgs e)
    {
        ListModeButton.IsChecked = true;
        BoardModeButton.IsChecked = false;
        UpdateSurfaceVisibility(VisibleIssues.Count == 0);
    }

    private void BoardMode_Click(object sender, RoutedEventArgs e)
    {
        ListModeButton.IsChecked = false;
        BoardModeButton.IsChecked = true;
        UpdateSurfaceVisibility(VisibleIssues.Count == 0);
    }

    private void IssueListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingSelection)
        {
            return;
        }

        if (IssueListView.SelectedItem is IssueItem issue)
        {
            SelectIssue(issue);
        }
    }

    private void FluentIssueListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingSelection)
        {
            return;
        }

        if (FluentIssueListView.SelectedItem is IssueItem issue)
        {
            SelectIssue(issue);
        }
    }

    private void BoardIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        var issue = _issues.FirstOrDefault(item => item.Id == id);
        if (issue is not null)
        {
            SelectIssue(issue);
        }
    }

    private async void SaveIssue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIssue is null)
        {
            return;
        }

        _selectedIssue.Title = NormalizeTitle(IssueTitleTextBox.Text);
        _selectedIssue.Description = IssueDescriptionTextBox.Text.Trim();
        _selectedIssue.Status = ReadSelectedTag(DetailStatusDropDownButton, "Todo");
        _selectedIssue.Priority = ReadSelectedTag(DetailPriorityDropDownButton, "Medium");
        _selectedIssue.Project = ReadSelectedTag(DetailProjectDropDownButton, "Platform");
        _selectedIssue.Assignee = ReadSelectedTag(DetailAssigneeDropDownButton, "나");
        _selectedIssue.DueDate = IssueDueDateTextBox.Text.Trim();
        _selectedIssue.Labels = IssueLabelsTextBox.Text.Trim();

        RefreshViews(keepSelection: true);
        LoadIssueIntoEditor(_selectedIssue);
        await SaveIssuesAsync();
    }

    private async void FluentSaveIssue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIssue is null)
        {
            return;
        }

        _selectedIssue.Title = NormalizeTitle(FluentIssueTitleTextBox.Text);
        _selectedIssue.Description = FluentIssueDescriptionTextBox.Text.Trim();
        _selectedIssue.Status = ReadSelectedComboBoxTag(FluentDetailStatusComboBox, "Todo");
        _selectedIssue.Priority = ReadSelectedComboBoxTag(FluentDetailPriorityComboBox, "Medium");
        _selectedIssue.Project = ReadSelectedComboBoxTag(FluentDetailProjectComboBox, "Platform");
        _selectedIssue.Assignee = ReadSelectedComboBoxTag(FluentDetailAssigneeComboBox, "나");
        _selectedIssue.DueDate = FluentIssueDueDateTextBox.Text.Trim();
        _selectedIssue.Labels = FluentIssueLabelsTextBox.Text.Trim();

        RefreshViews(keepSelection: true);
        LoadIssueIntoEditor(_selectedIssue);
        await SaveIssuesAsync();
    }

    private async void DeleteSelectedIssue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIssue is null)
        {
            return;
        }

        _issues.Remove(_selectedIssue);
        _selectedIssue = null;
        _isRefreshingSelection = true;
        IssueListView.SelectedItem = null;
        FluentIssueListView.SelectedItem = null;
        _isRefreshingSelection = false;
        UpdateDetailVisibility(hasSelection: false);
        RefreshViews();
        await SaveIssuesAsync();
    }

    private async Task AddIssueAsync(string title, bool selectAfterCreate)
    {
        title = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(title))
        {
            if (UsesNativeFluentSurface)
            {
                FluentQuickIssueTextBox.Focus(FocusState.Programmatic);
            }
            else
            {
                QuickIssueTextBox.Focus(FocusState.Programmatic);
            }

            return;
        }

        var issue = IssueItem.Create(NextIssueKey(), title);
        _issues.Insert(0, issue);
        QuickIssueTextBox.Text = string.Empty;
        FluentQuickIssueTextBox.Text = string.Empty;
        RefreshViews(keepSelection: false);

        if (selectAfterCreate)
        {
            SelectIssue(issue);
        }

        await SaveIssuesAsync();
    }

    private async Task LoadIssuesAsync()
    {
        _isLoading = true;

        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(IssuesFileName) is StorageFile issueFile)
            {
                var json = await FileIO.ReadTextAsync(issueFile);
                var snapshots = JsonSerializer.Deserialize<List<IssueSnapshot>>(json);
                LoadSnapshots(snapshots);
            }
            else if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(LegacyTodosFileName) is StorageFile legacyFile)
            {
                await LoadLegacyTodosAsync(legacyFile);
            }
            else
            {
                SeedIssues();
            }
        }
        catch
        {
            _issues.Clear();
            SeedIssues();
        }
        finally
        {
            _isLoading = false;
            RefreshViews();
            SelectIssue(VisibleIssues.FirstOrDefault() ?? _issues.FirstOrDefault());
        }
    }

    private async Task LoadLegacyTodosAsync(StorageFile legacyFile)
    {
        var json = await FileIO.ReadTextAsync(legacyFile);
        var todos = JsonSerializer.Deserialize<List<LegacyTodoSnapshot>>(json);
        if (todos is null || todos.Count == 0)
        {
            SeedIssues();
            return;
        }

        var index = 101;
        foreach (var todo in todos)
        {
            _issues.Add(new IssueItem(
                id: string.IsNullOrWhiteSpace(todo.Id) ? Guid.NewGuid().ToString("N") : todo.Id,
                key: $"TD-{index++}",
                title: NormalizeTitle(todo.Title),
                description: "기존 Todo 항목에서 가져온 이슈입니다.",
                status: todo.IsCompleted ? "Done" : "Todo",
                priority: "Medium",
                assignee: "나",
                project: "Platform",
                dueDate: string.Empty,
                labels: "migrated"));
        }
    }

    private void LoadSnapshots(List<IssueSnapshot>? snapshots)
    {
        _issues.Clear();
        if (snapshots is null || snapshots.Count == 0)
        {
            SeedIssues();
            return;
        }

        foreach (var snapshot in snapshots)
        {
            _issues.Add(IssueItem.FromSnapshot(snapshot));
        }
    }

    private void SeedIssues()
    {
        _issues.Clear();
        _issues.Add(new IssueItem(Guid.NewGuid().ToString("N"), "TD-101", "이슈 리스트와 상세 패널 정리", "리스트에서 바로 선택하고 오른쪽에서 상태, 담당자, 기한을 편집합니다.", "InProgress", "High", "나", "Platform", "2026-06-07", "ux,core"));
        _issues.Add(new IssueItem(Guid.NewGuid().ToString("N"), "TD-102", "보드 보기에서 업무 흐름 확인", "상태별 칼럼으로 백로그부터 완료까지 흐름을 확인합니다.", "Todo", "Medium", "Design", "Platform", "2026-06-10", "board"));
        _issues.Add(new IssueItem(Guid.NewGuid().ToString("N"), "TD-103", "릴리즈 체크리스트 작성", "배포 전에 아이콘, 패키지 이름, 실행 경로를 한 번 더 점검합니다.", "Backlog", "Low", "QA", "Ops", "2026-06-14", "release"));
        _issues.Add(new IssueItem(Guid.NewGuid().ToString("N"), "TD-104", "저장 데이터 마이그레이션", "기존 Todo 데이터를 새 이슈 모델로 옮기는 경로를 유지합니다.", "Review", "High", "Backend", "Platform", "2026-06-06", "data"));
        _issues.Add(new IssueItem(Guid.NewGuid().ToString("N"), "TD-105", "완료된 샘플 이슈", "완료 상태와 완료율 메트릭을 확인하기 위한 샘플입니다.", "Done", "Medium", "나", "Growth", "2026-06-03", "sample"));
    }

    private async Task SaveIssuesAsync()
    {
        if (_isLoading)
        {
            return;
        }

        var snapshots = _issues.Select(IssueSnapshot.FromItem).ToList();
        var json = JsonSerializer.Serialize(snapshots, _jsonOptions);
        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
            IssuesFileName,
            CreationCollisionOption.ReplaceExisting);

        await FileIO.WriteTextAsync(file, json);
    }

    private void RefreshViews(bool keepSelection = true)
    {
        var filtered = _issues.Where(MatchesCurrentScope).Where(MatchesFilters).ToList();

        ReplaceItems(VisibleIssues, filtered);
        ReplaceItems(BacklogIssues, filtered.Where(issue => issue.Status == "Backlog"));
        ReplaceItems(TodoIssues, filtered.Where(issue => issue.Status == "Todo"));
        ReplaceItems(InProgressIssues, filtered.Where(issue => issue.Status == "InProgress"));
        ReplaceItems(ReviewIssues, filtered.Where(issue => issue.Status == "Review"));
        ReplaceItems(DoneIssues, filtered.Where(issue => issue.Status == "Done"));

        UpdateMetrics(filtered);
        UpdateEmptyState(filtered.Count == 0);

        if (!keepSelection || _selectedIssue is null)
        {
            return;
        }

        if (!filtered.Contains(_selectedIssue))
        {
            SelectIssue(filtered.FirstOrDefault());
            return;
        }

        _isRefreshingSelection = true;
        IssueListView.SelectedItem = _selectedIssue;
        _isRefreshingSelection = false;
    }

    private bool MatchesCurrentScope(IssueItem issue)
    {
        return _navScope switch
        {
            "Mine" => issue.Assignee == "나",
            "Release" => issue.Labels.Contains("release", StringComparison.OrdinalIgnoreCase) || issue.Project == "Ops",
            "Done" => issue.Status == "Done",
            _ => true
        };
    }

    private bool MatchesFilters(IssueItem issue)
    {
        var query = _searchQuery;
        var status = _statusFilter;
        var priority = _priorityFilter;

        var matchesQuery = string.IsNullOrWhiteSpace(query)
            || issue.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || issue.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || issue.Assignee.Contains(query, StringComparison.OrdinalIgnoreCase)
            || issue.Project.Contains(query, StringComparison.OrdinalIgnoreCase)
            || issue.Labels.Contains(query, StringComparison.OrdinalIgnoreCase)
            || issue.Key.Contains(query, StringComparison.OrdinalIgnoreCase);

        var matchesStatus = status == "All" || issue.Status == status;
        var matchesPriority = priority == "All" || issue.Priority == priority;

        return matchesQuery && matchesStatus && matchesPriority;
    }

    private void UpdateMetrics(IReadOnlyCollection<IssueItem> filtered)
    {
        var total = filtered.Count;
        var active = filtered.Count(issue => issue.Status is "Todo" or "InProgress");
        var review = filtered.Count(issue => issue.Status == "Review");
        var done = filtered.Count(issue => issue.Status == "Done");
        var completion = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);

        TotalMetricTextBlock.Text = total.ToString();
        ActiveMetricTextBlock.Text = active.ToString();
        ReviewMetricTextBlock.Text = review.ToString();
        CompletionMetricTextBlock.Text = $"{completion}%";
        ScopeTextBlock.Text = $"{ScopeLabel} · {filtered.Count}개 이슈";
        FluentScopeTextBlock.Text = $"{ScopeLabel} · {filtered.Count}개 이슈";
    }

    private void UpdateEmptyState(bool isEmpty)
    {
        UpdateSurfaceVisibility(isEmpty);
    }

    private void UpdateSurfaceVisibility(bool isEmpty)
    {
        var showBoard = !isEmpty && BoardModeButton.IsChecked == true;
        var showList = !isEmpty && !showBoard;

        if (!_hasAnimatedViewSurface)
        {
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            ListHeaderBar.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
            IssueListView.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
            BoardHost.Visibility = showBoard ? Visibility.Visible : Visibility.Collapsed;
            FluentEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            FluentIssueListView.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

            PrepareVisibleSurface(ListHeaderBar, showList);
            PrepareVisibleSurface(IssueListView, showList);
            PrepareVisibleSurface(BoardHost, showBoard);
            PrepareVisibleSurface(EmptyState, isEmpty);
            _hasAnimatedViewSurface = true;
            return;
        }

        TransitionSurface(EmptyState, isEmpty, incomingTranslateY: 10, incomingScale: 0.992, incomingSkewX: 0.0);
        TransitionSurface(ListHeaderBar, showList, incomingTranslateY: -6, incomingScale: 1.006, incomingSkewX: -0.18);
        TransitionSurface(IssueListView, showList, incomingTranslateY: 10, incomingScale: 0.990, incomingSkewX: 0.22);
        TransitionSurface(BoardHost, showBoard, incomingTranslateY: 14, incomingScale: 0.986, incomingSkewX: -0.34);
        FluentEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        FluentIssueListView.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateDetailVisibility(bool hasSelection)
    {
        DetailEditor.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        DetailEmptyState.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        FluentDetailEditor.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        FluentDetailEmptyState.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncNavigationSelection()
    {
        _isSyncingNavigation = true;
        try
        {
            SelectNavigationItemByTag(ShellNavigation, _navScope);
            SelectNavigationItemByTag(FluentNativeRoot, _navScope);
        }
        finally
        {
            _isSyncingNavigation = false;
        }
    }

    private static void SelectNavigationItemByTag(NavigationView navigationView, string tag)
    {
        foreach (var item in navigationView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string itemTag && itemTag == tag)
            {
                navigationView.SelectedItem = item;
                return;
            }
        }
    }

    private void SyncNativeFilterControls()
    {
        _isSyncingNativeControls = true;
        try
        {
            SelectComboBoxByTag(FluentStatusFilterComboBox, _statusFilter);
            SelectComboBoxByTag(FluentPriorityFilterComboBox, _priorityFilter);
            SelectDropDownByTag(StatusFilterDropDownButton, _statusFilter);
            SelectDropDownByTag(PriorityFilterDropDownButton, _priorityFilter);
        }
        finally
        {
            _isSyncingNativeControls = false;
        }
    }

    private void SyncSearchTextBoxes(TextBox? source = null)
    {
        _isSyncingNativeControls = true;
        try
        {
            if (source != SearchTextBox && SearchTextBox.Text != _searchQuery)
            {
                SearchTextBox.Text = _searchQuery;
            }

            if (source != FluentSearchTextBox && FluentSearchTextBox.Text != _searchQuery)
            {
                FluentSearchTextBox.Text = _searchQuery;
            }
        }
        finally
        {
            _isSyncingNativeControls = false;
        }
    }

    private static void PrepareVisibleSurface(FrameworkElement element, bool isVisible)
    {
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.Opacity = isVisible ? 1 : 0;
        EnsureCompositeTransform(element);

        if (element.RenderTransform is CompositeTransform transform)
        {
            transform.TranslateY = 0;
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.SkewX = 0;
        }
    }

    private static void TransitionSurface(
        FrameworkElement element,
        bool show,
        double incomingTranslateY,
        double incomingScale,
        double incomingSkewX)
    {
        EnsureCompositeTransform(element);

        if (show)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            if (element.RenderTransform is CompositeTransform startTransform)
            {
                startTransform.TranslateY = incomingTranslateY;
                startTransform.ScaleX = incomingScale;
                startTransform.ScaleY = incomingScale;
                startTransform.SkewX = incomingSkewX;
            }

            AnimateViewSurface(element, opacity: 1, translateY: 0, scale: 1, skewX: 0, durationMilliseconds: 230, collapseWhenDone: false);
            return;
        }

        if (element.Visibility != Visibility.Visible)
        {
            element.Opacity = 0;
            return;
        }

        AnimateViewSurface(
            element,
            opacity: 0,
            translateY: incomingTranslateY * -0.45,
            scale: 0.992,
            skewX: incomingSkewX * -0.35,
            durationMilliseconds: 150,
            collapseWhenDone: true);
    }

    private static void AnimateViewSurface(
        FrameworkElement element,
        double opacity,
        double translateY,
        double scale,
        double skewX,
        double durationMilliseconds,
        bool collapseWhenDone)
    {
        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        AddDoubleAnimation(storyboard, element, "Opacity", opacity, duration, easing, enableDependentAnimation: false);
        AddDoubleAnimation(storyboard, element, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)", translateY, duration, easing, enableDependentAnimation: true);
        AddDoubleAnimation(storyboard, element, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)", scale, duration, easing, enableDependentAnimation: true);
        AddDoubleAnimation(storyboard, element, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)", scale, duration, easing, enableDependentAnimation: true);
        AddDoubleAnimation(storyboard, element, "(UIElement.RenderTransform).(CompositeTransform.SkewX)", skewX, duration, easing, enableDependentAnimation: true);

        if (collapseWhenDone)
        {
            storyboard.Completed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
            };
        }

        storyboard.Begin();
    }

    private static void EnsureCompositeTransform(FrameworkElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is not CompositeTransform)
        {
            element.RenderTransform = new CompositeTransform();
        }
    }

    private static void AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string targetProperty,
        double to,
        Duration duration,
        EasingFunctionBase easing,
        bool enableDependentAnimation)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EnableDependentAnimation = enableDependentAnimation,
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, targetProperty);
        storyboard.Children.Add(animation);
    }

    private void SelectIssue(IssueItem? issue)
    {
        if (_selectedIssue is not null && _selectedIssue != issue)
        {
            _selectedIssue.IsSelected = false;
        }

        _selectedIssue = issue;

        if (_selectedIssue is not null)
        {
            _selectedIssue.IsSelected = true;
        }

        _isRefreshingSelection = true;
        IssueListView.SelectedItem = issue;
        FluentIssueListView.SelectedItem = issue;
        _isRefreshingSelection = false;

        if (issue is null)
        {
            UpdateDetailVisibility(hasSelection: false);
            return;
        }

        UpdateDetailVisibility(hasSelection: true);
        LoadIssueIntoEditor(issue);
    }

    private void LoadIssueIntoEditor(IssueItem issue)
    {
        SelectedKeyTextBlock.Text = $"{issue.Key} · {issue.StatusLabel}";
        SelectedStatusTextBlock.Text = issue.PriorityLabel;
        IssueTitleTextBox.Text = issue.Title;
        IssueDescriptionTextBox.Text = issue.Description;
        IssueDueDateTextBox.Text = issue.DueDate;
        IssueLabelsTextBox.Text = issue.Labels;
        DetailHintTextBlock.Text = $"마지막 선택: {issue.Project} / {issue.Assignee}";

        SelectDropDownByTag(DetailStatusDropDownButton, issue.Status);
        SelectDropDownByTag(DetailPriorityDropDownButton, issue.Priority);
        SelectDropDownByTag(DetailProjectDropDownButton, issue.Project);
        SelectDropDownByTag(DetailAssigneeDropDownButton, issue.Assignee);

        FluentSelectedKeyTextBlock.Text = $"{issue.Key} · {issue.StatusLabel}";
        FluentSelectedStatusTextBlock.Text = issue.PriorityLabel;
        FluentIssueTitleTextBox.Text = issue.Title;
        FluentIssueDescriptionTextBox.Text = issue.Description;
        FluentIssueDueDateTextBox.Text = issue.DueDate;
        FluentIssueLabelsTextBox.Text = issue.Labels;
        FluentDetailHintTextBlock.Text = $"마지막 선택: {issue.Project} / {issue.Assignee}";

        SelectComboBoxByTag(FluentDetailStatusComboBox, issue.Status);
        SelectComboBoxByTag(FluentDetailPriorityComboBox, issue.Priority);
        SelectComboBoxByTag(FluentDetailProjectComboBox, issue.Project);
        SelectComboBoxByTag(FluentDetailAssigneeComboBox, issue.Assignee);
    }

    private string NextIssueKey()
    {
        var max = _issues
            .Select(issue => issue.Key)
            .Select(key => key.StartsWith("TD-", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[3..], out var value) ? value : 100)
            .DefaultIfEmpty(100)
            .Max();

        return $"TD-{max + 1}";
    }

    private string ScopeLabel => _navScope switch
    {
        "Mine" => "내 작업",
        "Release" => "릴리즈",
        "Done" => "완료",
        _ => "전체 이슈"
    };

    private static void ReplaceItems(ObservableCollection<IssueItem> target, IEnumerable<IssueItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string NormalizeTitle(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string ReadSelectedTag(DropDownButton button, string fallback)
    {
        return button.Tag is string tag && !string.IsNullOrWhiteSpace(tag) ? tag : fallback;
    }

    private void UpdateDropDownSelection(DropDownButton button, object source)
    {
        if (!TryGetDropDownItemData(source, out var text, out var tag))
        {
            return;
        }

        button.Content = text;
        button.Tag = !string.IsNullOrWhiteSpace(tag) ? tag : text;
        RefreshDropDownMenuSelection(button);
    }

    private void SelectDropDownByTag(DropDownButton button, string tag)
    {
        if (TryGetOverlayForButton(button, out var overlay) && TrySelectDropDownByTag(overlay, button, tag))
        {
            return;
        }

        button.Content = tag;
        button.Tag = tag;
        RefreshDropDownMenuSelection(button);
    }

    private static string ReadSelectedComboBoxTag(ComboBox comboBox, string fallback)
    {
        return TryReadSelectedComboBoxTag(comboBox, out var tag) ? tag : fallback;
    }

    private static bool TryReadSelectedComboBoxTag(ComboBox comboBox, out string tag)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            tag = item.Tag as string ?? item.Content?.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(tag);
        }

        tag = string.Empty;
        return false;
    }

    private void SelectComboBoxByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            var itemTag = item.Tag as string ?? item.Content?.ToString();
            if (itemTag == tag)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private bool TrySelectDropDownByTag(DependencyObject root, DropDownButton button, string tag)
    {
        foreach (var item in EnumerateFlyoutButtons(root))
        {
            var itemText = item.Content?.ToString() ?? string.Empty;
            var itemTag = item.Tag as string ?? itemText;
            if (itemTag == tag)
            {
                button.Content = itemText;
                button.Tag = itemTag;
                RefreshDropDownMenuSelection(button);
                return true;
            }
        }

        return false;
    }

    private void RefreshDropDownMenuSelection(DropDownButton button)
    {
        if (TryGetOverlayForButton(button, out var overlay))
        {
            RefreshDropDownButtonSelection(overlay, button);
            return;
        }

    }

    private bool TryGetOverlayForButton(DropDownButton button, out DependencyObject overlay)
    {
        if (button == ThemeDropDownButton)
        {
            overlay = ThemeOverlay;
            return true;
        }

        if (button == StatusFilterDropDownButton)
        {
            overlay = StatusFilterOverlay;
            return true;
        }

        if (button == PriorityFilterDropDownButton)
        {
            overlay = PriorityFilterOverlay;
            return true;
        }

        if (button == DetailStatusDropDownButton)
        {
            overlay = DetailStatusOverlay;
            return true;
        }

        if (button == DetailPriorityDropDownButton)
        {
            overlay = DetailPriorityOverlay;
            return true;
        }

        if (button == DetailProjectDropDownButton)
        {
            overlay = DetailProjectOverlay;
            return true;
        }

        if (button == DetailAssigneeDropDownButton)
        {
            overlay = DetailAssigneeOverlay;
            return true;
        }

        overlay = null!;
        return false;
    }

    private static void RefreshDropDownButtonSelection(DependencyObject root, DropDownButton button)
    {
        var selectedTag = ReadSelectedTag(button, string.Empty);
        foreach (var item in EnumerateFlyoutButtons(root))
        {
            var itemText = item.Content?.ToString() ?? string.Empty;
            var itemTag = item.Tag as string ?? itemText;
            var isSelected = string.Equals(itemTag, selectedTag, StringComparison.Ordinal);
            item.Background = isSelected ? CreateSelectedMenuItemBrush(button.ActualTheme) : new SolidColorBrush(Colors.Transparent);
            item.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static bool TryGetDropDownItemData(object source, out string text, out string tag)
    {
        if (source is Button button)
        {
            text = button.Content?.ToString() ?? string.Empty;
            tag = button.Tag as string ?? text;
            return true;
        }

        text = string.Empty;
        tag = string.Empty;
        return false;
    }

    private static IEnumerable<Button> EnumerateFlyoutButtons(DependencyObject root)
    {
        if (root is Button rootButton)
        {
            yield return rootButton;
        }

        if (root is Border { Child: DependencyObject borderChild })
        {
            foreach (var descendant in EnumerateFlyoutButtons(borderChild))
            {
                yield return descendant;
            }
        }

        if (root is Panel panel)
        {
            foreach (var child in panel.Children.OfType<DependencyObject>())
            {
                foreach (var descendant in EnumerateFlyoutButtons(child))
                {
                    yield return descendant;
                }
            }
        }

        if (root is ContentControl { Content: DependencyObject content })
        {
            foreach (var descendant in EnumerateFlyoutButtons(content))
            {
                yield return descendant;
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var descendant in EnumerateFlyoutButtons(child))
            {
                yield return descendant;
            }
        }
    }

    private static SolidColorBrush CreateSelectedMenuItemBrush(ElementTheme theme)
    {
        var resolvedTheme = theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark
            : theme;

        return new SolidColorBrush(resolvedTheme == ElementTheme.Dark
            ? Color.FromArgb(0x34, 98, 154, 255)
            : Color.FromArgb(0x24, 24, 140, 255));
    }
}

public sealed class IssueItem : INotifyPropertyChanged
{
    private string _title;
    private string _description;
    private string _status;
    private string _priority;
    private string _assignee;
    private string _project;
    private string _dueDate;
    private string _labels;
    private bool _isSelected;

    public IssueItem(
        string id,
        string key,
        string title,
        string description,
        string status,
        string priority,
        string assignee,
        string project,
        string dueDate,
        string labels)
    {
        Id = id;
        Key = key;
        _title = title;
        _description = description;
        _status = status;
        _priority = priority;
        _assignee = assignee;
        _project = project;
        _dueDate = dueDate;
        _labels = labels;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Key { get; }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(Metadata));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string Priority
    {
        get => _priority;
        set
        {
            if (SetField(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityLabel));
                OnPropertyChanged(nameof(PriorityToken));
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string Assignee
    {
        get => _assignee;
        set
        {
            if (SetField(ref _assignee, value))
            {
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string Project
    {
        get => _project;
        set
        {
            if (SetField(ref _project, value))
            {
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string DueDate
    {
        get => _dueDate;
        set
        {
            if (SetField(ref _dueDate, value))
            {
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string Labels
    {
        get => _labels;
        set
        {
            if (SetField(ref _labels, value))
            {
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionFillOpacity));
            OnPropertyChanged(nameof(SelectionRingOpacity));
        }
    }

    public string StatusLabel => Status switch
    {
        "Backlog" => "백로그",
        "Todo" => "예정",
        "InProgress" => "진행 중",
        "Review" => "리뷰",
        "Done" => "완료",
        _ => Status
    };

    public string PriorityLabel => Priority switch
    {
        "Urgent" => "긴급",
        "High" => "높음",
        "Medium" => "보통",
        "Low" => "낮음",
        _ => Priority
    };

    public string Metadata => $"{Project} · {Assignee} · {Labels}";

    public SolidColorBrush StatusBrush => new(Status switch
    {
        "Backlog" => Color.FromArgb(255, 142, 142, 147),
        "Todo" => Color.FromArgb(255, 10, 132, 255),
        "InProgress" => Color.FromArgb(255, 255, 159, 10),
        "Review" => Color.FromArgb(255, 191, 90, 242),
        "Done" => Color.FromArgb(255, 50, 215, 75),
        _ => Color.FromArgb(255, 142, 142, 147)
    });

    public string PriorityToken => Priority switch
    {
        "Urgent" => "P0",
        "High" => "P1",
        "Medium" => "P2",
        "Low" => "P3",
        _ => "P?"
    };

    public string RowSubtitle => $"{PriorityToken} · {Project} · {Labels}";

    public string AutomationName => $"{Key} {Title} {StatusLabel}";

    public double SelectionFillOpacity => IsSelected ? 0.10 : 0;

    public double SelectionRingOpacity => IsSelected ? 0.30 : 0;

    public static IssueItem Create(string key, string title)
    {
        return new IssueItem(
            Guid.NewGuid().ToString("N"),
            key,
            title,
            "새로 생성된 이슈입니다. 오른쪽 패널에서 설명과 메타데이터를 정리하세요.",
            "Todo",
            "Medium",
            "나",
            "Platform",
            string.Empty,
            "triage");
    }

    public static IssueItem FromSnapshot(IssueSnapshot snapshot)
    {
        return new IssueItem(
            string.IsNullOrWhiteSpace(snapshot.Id) ? Guid.NewGuid().ToString("N") : snapshot.Id,
            string.IsNullOrWhiteSpace(snapshot.Key) ? "TD-100" : snapshot.Key,
            string.IsNullOrWhiteSpace(snapshot.Title) ? "제목 없음" : snapshot.Title,
            snapshot.Description ?? string.Empty,
            string.IsNullOrWhiteSpace(snapshot.Status) ? "Todo" : snapshot.Status,
            string.IsNullOrWhiteSpace(snapshot.Priority) ? "Medium" : snapshot.Priority,
            string.IsNullOrWhiteSpace(snapshot.Assignee) ? "나" : snapshot.Assignee,
            string.IsNullOrWhiteSpace(snapshot.Project) ? "Platform" : snapshot.Project,
            snapshot.DueDate ?? string.Empty,
            snapshot.Labels ?? string.Empty);
    }

    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record IssueSnapshot(
    string Id,
    string Key,
    string Title,
    string Description,
    string Status,
    string Priority,
    string Assignee,
    string Project,
    string DueDate,
    string Labels)
{
    public static IssueSnapshot FromItem(IssueItem item)
    {
        return new IssueSnapshot(
            item.Id,
            item.Key,
            item.Title,
            item.Description,
            item.Status,
            item.Priority,
            item.Assignee,
            item.Project,
            item.DueDate,
            item.Labels);
    }
}

public sealed record LegacyTodoSnapshot(string Id, string Title, bool IsCompleted);
