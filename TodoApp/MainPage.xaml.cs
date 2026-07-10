using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TodoApp;

public sealed partial class MainPage : Page
{
    private readonly IssueWorkspaceViewModel _viewModel = new();
    private readonly IssueRepository _issueRepository = new();
    private readonly UISettings _uiSettings = new();
    private Task? _initialLoadTask;
    private IssueItem? _selectedIssue;
    private bool _isLoading;
    private bool _isRefreshingSelection;
    private bool _isSyncingNativeControls;
    private bool _isSyncingNavigation;
    private bool _hasAnimatedViewSurface;
    private AppVisualTheme _visualTheme = AppVisualTheme.Liquid;
    private bool _isNarrowLayout;
    private bool _isShowingNarrowDetail;

    public ObservableCollection<IssueItem> VisibleIssues => _viewModel.VisibleIssues;
    public ObservableCollection<IssueItem> BacklogIssues => _viewModel.BacklogIssues;
    public ObservableCollection<IssueItem> TodoIssues => _viewModel.TodoIssues;
    public ObservableCollection<IssueItem> InProgressIssues => _viewModel.InProgressIssues;
    public ObservableCollection<IssueItem> ReviewIssues => _viewModel.ReviewIssues;
    public ObservableCollection<IssueItem> DoneIssues => _viewModel.DoneIssues;

    public MainPage()
    {
        _visualTheme = VisualThemeManager.LoadSavedTheme();
        VisualThemeManager.Apply(_visualTheme);

        InitializeComponent();
        ApplyLocalizedAutomationProperties();

        VisualThemeManager.ThemeApplied += VisualThemeManager_ThemeApplied;
        Unloaded += Page_Unloaded;
        UpdateThemeDropDownSelection();
        ShellNavigation.SelectedItem = AllIssuesNavItem;
        SelectDropDownByTag(StatusFilterDropDownButton, "All");
        SelectDropDownByTag(PriorityFilterDropDownButton, "All");
        SelectComboBoxByTag(FluentStatusFilterComboBox, _viewModel.StatusFilter);
        SelectComboBoxByTag(FluentPriorityFilterComboBox, _viewModel.PriorityFilter);
        UpdateThemeSurfaceMode();
        SetLoadingState(isLoading: true);
        _initialLoadTask = LoadIssuesAsync();
    }

    private void ApplyLocalizedAutomationProperties()
    {
        AutomationProperties.SetName(QuickIssueTextBox, AppResources.Get("NewIssueTitle"));
        AutomationProperties.SetName(FluentQuickIssueTextBox, AppResources.Get("NewIssueTitle"));
        AutomationProperties.SetName(SearchTextBox, AppResources.Get("SearchName"));
        AutomationProperties.SetName(FluentSearchTextBox, AppResources.Get("SearchName"));
        AutomationProperties.SetName(StatusFilterDropDownButton, AppResources.Get("StatusFilterName"));
        AutomationProperties.SetName(FluentStatusFilterComboBox, AppResources.Get("StatusFilterName"));
        AutomationProperties.SetName(PriorityFilterDropDownButton, AppResources.Get("PriorityFilterName"));
        AutomationProperties.SetName(FluentPriorityFilterComboBox, AppResources.Get("PriorityFilterName"));
        AutomationProperties.SetName(FluentThemeComboBox, AppResources.Get("ThemeFilterName"));
        AutomationProperties.SetName(ThemeDropDownButton, AppResources.Get("ThemeFilterName"));
        AutomationProperties.SetName(IssueDueDatePicker, AppResources.Get("DueDateName"));
        AutomationProperties.SetName(FluentIssueDueDatePicker, AppResources.Get("DueDateName"));
        AutomationProperties.SetName(CustomNewIssueButton, AppResources.Get("NewIssueTitle"));
        AutomationProperties.SetName(ListModeButton, AppResources.Get("ListViewName"));
        AutomationProperties.SetName(BoardModeButton, AppResources.Get("BoardViewName"));
        AutomationProperties.SetName(CustomAddQuickIssueButton, AppResources.Get("AddIssueName"));
        AutomationProperties.SetName(CustomSaveIssueButton, AppResources.Get("SaveIssueName"));
        AutomationProperties.SetName(CustomDeleteIssueButton, AppResources.Get("DeleteIssueName"));
        AutomationProperties.SetName(LoadingProgressRing, AppResources.Get("LoadingTitle"));
        ToolTipService.SetToolTip(CustomNewIssueButton, AppResources.Get("NewIssueTitle"));
        ToolTipService.SetToolTip(ThemeDropDownButton, AppResources.Get("ThemeFilterName"));
        ToolTipService.SetToolTip(ListModeButton, AppResources.Get("ListViewName"));
        ToolTipService.SetToolTip(BoardModeButton, AppResources.Get("BoardViewName"));
        ToolTipService.SetToolTip(CustomAddQuickIssueButton, AppResources.Get("AddIssueName"));
        ToolTipService.SetToolTip(CustomSaveIssueButton, AppResources.Get("SaveIssueName"));
        ToolTipService.SetToolTip(CustomDeleteIssueButton, AppResources.Get("DeleteIssueName"));
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialLoadTask is not null)
        {
            await _initialLoadTask;
        }

        if (_visualTheme != AppVisualTheme.Liquid)
        {
            DispatcherQueue.TryEnqueue(() => ApplyVisualTheme(_visualTheme, save: false));
        }

        UpdateThemeSurfaceMode();
        ApplyResponsiveLayout(ActualWidth);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        VisualThemeManager.ThemeApplied -= VisualThemeManager_ThemeApplied;
        Unloaded -= Page_Unloaded;
    }

    private void ApplyResponsiveLayout(double width)
    {
        var useNarrowLayout = width > 0 && width < 840;
        if (_isNarrowLayout == useNarrowLayout && width > 0)
        {
            return;
        }

        _isNarrowLayout = useNarrowLayout;
        ShellNavigation.PaneDisplayMode = useNarrowLayout
            ? NavigationViewPaneDisplayMode.LeftMinimal
            : NavigationViewPaneDisplayMode.Auto;
        FluentNativeRoot.PaneDisplayMode = useNarrowLayout
            ? NavigationViewPaneDisplayMode.LeftMinimal
            : NavigationViewPaneDisplayMode.Auto;

        CustomContentRoot.Padding = useNarrowLayout ? new Thickness(8) : new Thickness(18, 14, 18, 18);
        FluentContentRoot.Padding = useNarrowLayout ? new Thickness(12) : new Thickness(24, 18, 24, 24);
        CustomSplitGrid.ColumnSpacing = useNarrowLayout ? 0 : 12;
        FluentSplitGrid.ColumnSpacing = useNarrowLayout ? 0 : 16;

        Grid.SetColumn(CustomDetailPane, useNarrowLayout ? 0 : 1);
        Grid.SetColumn(FluentDetailPane, useNarrowLayout ? 0 : 1);
        CustomDetailColumn.Width = useNarrowLayout ? new GridLength(0) : new GridLength(320);
        FluentDetailColumn.Width = useNarrowLayout ? new GridLength(0) : new GridLength(360);
        CustomDetailPane.MinWidth = useNarrowLayout ? 0 : 320;
        CustomBackToListButton.Visibility = useNarrowLayout ? Visibility.Visible : Visibility.Collapsed;
        FluentBackToListButton.Visibility = useNarrowLayout ? Visibility.Visible : Visibility.Collapsed;

        if (!useNarrowLayout)
        {
            _isShowingNarrowDetail = false;
        }

        UpdateNarrowSurfaceVisibility();
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        _isShowingNarrowDetail = false;
        UpdateNarrowSurfaceVisibility();
        (UsesNativeFluentSurface ? FluentIssueListView : IssueListView).Focus(FocusState.Programmatic);
    }

    private void UpdateNarrowSurfaceVisibility()
    {
        if (!_isNarrowLayout)
        {
            CustomIssueSurface.Visibility = Visibility.Visible;
            CustomDetailPane.Visibility = Visibility.Visible;
            FluentIssueSurface.Visibility = Visibility.Visible;
            FluentDetailPane.Visibility = Visibility.Visible;
            return;
        }

        var showDetail = _isShowingNarrowDetail && _selectedIssue is not null;
        CustomIssueSurface.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        CustomDetailPane.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        FluentIssueSurface.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        FluentDetailPane.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
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

    private void AnimateGlassSurface(object sender, double translateY, double opacity, double scale, double skewX, double durationMilliseconds)
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

        if (!_uiSettings.AnimationsEnabled)
        {
            element.Opacity = opacity;
            if (element.RenderTransform is CompositeTransform finalTransform)
            {
                finalTransform.TranslateY = translateY;
                finalTransform.ScaleX = scale;
                finalTransform.ScaleY = scale;
                finalTransform.SkewX = skewX;
            }

            return;
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
        await EnsureInitialLoadAsync();
        await AddIssueAsync(AppResources.Get("NewIssueTitle"), selectAfterCreate: true);
    }

    private async void AddQuickIssue_Click(object sender, RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
        await AddIssueAsync(QuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private async void QuickIssueTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await EnsureInitialLoadAsync();
        await AddIssueAsync(QuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _viewModel.SearchQuery = SearchTextBox.Text.Trim();
        SyncSearchTextBoxes(SearchTextBox);
        RefreshViews();
    }

    private void FluentSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _viewModel.SearchQuery = FluentSearchTextBox.Text.Trim();
        SyncSearchTextBoxes(FluentSearchTextBox);
        RefreshViews();
    }

    private async void FluentNewIssue_Click(object sender, RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
        await AddIssueAsync(AppResources.Get("NewIssueTitle"), selectAfterCreate: true);
    }

    private async void FluentAddQuickIssue_Click(object sender, RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
        await AddIssueAsync(FluentQuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private async void FluentQuickIssueTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await EnsureInitialLoadAsync();
        await AddIssueAsync(FluentQuickIssueTextBox.Text, selectAfterCreate: true);
    }

    private void FluentStatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _viewModel.StatusFilter = ReadSelectedComboBoxTag(FluentStatusFilterComboBox, "All");
        SelectDropDownByTag(StatusFilterDropDownButton, _viewModel.StatusFilter);
        RefreshViews();
    }

    private void FluentPriorityFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingNativeControls)
        {
            return;
        }

        _viewModel.PriorityFilter = ReadSelectedComboBoxTag(FluentPriorityFilterComboBox, "All");
        SelectDropDownByTag(PriorityFilterDropDownButton, _viewModel.PriorityFilter);
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

    private static bool UsesNativeFluentSurface => VisualThemeManager.CurrentDefinition.WindowTreatment == AppWindowTreatment.Opaque;

    private void UpdateThemeSurfaceMode()
    {
        var definition = VisualThemeManager.CurrentDefinition;
        var useNativeFluent = definition.WindowTreatment == AppWindowTreatment.Opaque;
        CustomThemeRoot.Visibility = useNativeFluent ? Visibility.Collapsed : Visibility.Visible;
        FluentNativeRoot.Visibility = useNativeFluent ? Visibility.Visible : Visibility.Collapsed;
        UpdateDecorativeGlassLayers(definition);

        if (useNativeFluent)
        {
            CloseFilterOverlay();
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

    private void UpdateDecorativeGlassLayers(ThemeDefinition definition)
    {
        var showDecorativeLayers = !UsesNativeFluentSurface && definition.UsesDecorativeGlassLayers;
        var visibility = showDecorativeLayers ? Visibility.Visible : Visibility.Collapsed;

        ContentInfusionLayer.Visibility = visibility;
        PrimaryRefractionLayer.Visibility = visibility;
        SecondaryRefractionLayer.Visibility = visibility;
        SheenRefractionLayer.Visibility = visibility;
        DistortionRefractionLayer.Visibility = visibility;

        if (CustomThemeRoot.Resources["LiquidGlassDriftStoryboard"] is not Storyboard storyboard)
        {
            return;
        }

        if (showDecorativeLayers)
        {
            storyboard.Begin();
        }
        else
        {
            storyboard.Stop();
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
        _viewModel.StatusFilter = ReadSelectedTag(StatusFilterDropDownButton, "All");
        SelectComboBoxByTag(FluentStatusFilterComboBox, _viewModel.StatusFilter);
        CloseFilterOverlay();
        RefreshViews();
    }

    private void PriorityFilterMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        UpdateDropDownSelection(PriorityFilterDropDownButton, sender);
        _viewModel.PriorityFilter = ReadSelectedTag(PriorityFilterDropDownButton, "All");
        SelectComboBoxByTag(FluentPriorityFilterComboBox, _viewModel.PriorityFilter);
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
            _viewModel.NavigationScope = tag;
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
            _viewModel.NavigationScope = tag;
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

        var issue = _viewModel.Issues.FirstOrDefault(item => item.Id == id);
        if (issue is not null)
        {
            SelectIssue(issue);
        }
    }

    private async void SaveIssue_Click(object sender, RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
        if (_selectedIssue is null)
        {
            return;
        }

        var editedIssue = _selectedIssue;
        editedIssue.Title = NormalizeTitle(IssueTitleTextBox.Text);
        editedIssue.Description = IssueDescriptionTextBox.Text.Trim();
        editedIssue.Status = ReadSelectedTag(DetailStatusDropDownButton, "Todo");
        editedIssue.Priority = ReadSelectedTag(DetailPriorityDropDownButton, "Medium");
        editedIssue.Project = ReadSelectedTag(DetailProjectDropDownButton, "Platform");
        editedIssue.Assignee = ReadSelectedTag(DetailAssigneeDropDownButton, "Me");
        editedIssue.DueDate = FormatStorageDate(IssueDueDatePicker.Date);
        editedIssue.Labels = IssueLabelsTextBox.Text.Trim();

        RefreshViews(keepSelection: true);
        if (ReferenceEquals(_selectedIssue, editedIssue))
        {
            LoadIssueIntoEditor(editedIssue);
        }

        await TrySaveIssuesAsync();
    }

    private async void FluentSaveIssue_Click(object sender, RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
        if (_selectedIssue is null)
        {
            return;
        }

        var editedIssue = _selectedIssue;
        editedIssue.Title = NormalizeTitle(FluentIssueTitleTextBox.Text);
        editedIssue.Description = FluentIssueDescriptionTextBox.Text.Trim();
        editedIssue.Status = ReadSelectedComboBoxTag(FluentDetailStatusComboBox, "Todo");
        editedIssue.Priority = ReadSelectedComboBoxTag(FluentDetailPriorityComboBox, "Medium");
        editedIssue.Project = ReadSelectedComboBoxTag(FluentDetailProjectComboBox, "Platform");
        editedIssue.Assignee = ReadSelectedComboBoxTag(FluentDetailAssigneeComboBox, "Me");
        editedIssue.DueDate = FormatStorageDate(FluentIssueDueDatePicker.Date);
        editedIssue.Labels = FluentIssueLabelsTextBox.Text.Trim();

        RefreshViews(keepSelection: true);
        if (ReferenceEquals(_selectedIssue, editedIssue))
        {
            LoadIssueIntoEditor(editedIssue);
        }

        await TrySaveIssuesAsync();
    }

    private async void DeleteSelectedIssue_Click(object sender, RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
        if (_selectedIssue is null)
        {
            return;
        }

        var issueToDelete = _selectedIssue;
        var confirmationDialog = new ContentDialog
        {
            Title = AppResources.Format("DeleteDialogTitle", issueToDelete.Title),
            Content = AppResources.Get("DeleteDialogMessage"),
            PrimaryButtonText = AppResources.Get("DeleteButtonText"),
            CloseButtonText = AppResources.Get("CancelButtonText"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirmationDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var originalIndex = _viewModel.Issues.IndexOf(issueToDelete);
        _viewModel.Issues.Remove(issueToDelete);
        _selectedIssue = null;
        _isRefreshingSelection = true;
        IssueListView.SelectedItem = null;
        FluentIssueListView.SelectedItem = null;
        _isRefreshingSelection = false;
        UpdateDetailVisibility(hasSelection: false);
        RefreshViews();
        if (!await TrySaveIssuesAsync())
        {
            _viewModel.Issues.Insert(Math.Clamp(originalIndex, 0, _viewModel.Issues.Count), issueToDelete);
            RefreshViews(keepSelection: false);
            SelectIssue(issueToDelete);
            ShowStatus(
                InfoBarSeverity.Warning,
                AppResources.Get("DeleteFailedTitle"),
                AppResources.Get("DeleteFailedMessage"));
        }
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

        var issue = IssueItem.Create(_viewModel.NextIssueKey(), title);
        _viewModel.Issues.Insert(0, issue);
        QuickIssueTextBox.Text = string.Empty;
        FluentQuickIssueTextBox.Text = string.Empty;
        RefreshViews(keepSelection: false);

        if (selectAfterCreate)
        {
            SelectIssue(issue);
        }

        await TrySaveIssuesAsync();
    }

    private async Task LoadIssuesAsync()
    {
        _isLoading = true;

        try
        {
            var result = await _issueRepository.LoadAsync();
            _viewModel.Issues.Clear();
            foreach (var issue in result.Issues)
            {
                _viewModel.Issues.Add(issue);
            }

            if (result.Status == IssueLoadStatus.RecoveredBackup)
            {
                ShowStatus(
                    InfoBarSeverity.Warning,
                    AppResources.Get("BackupRecoveredTitle"),
                    AppResources.Format(
                        "BackupRecoveredMessage",
                        result.PreservedFileName ?? AppResources.Get("UntitledIssue")));
            }
            else if (result.Status == IssueLoadStatus.RecoveredWithSamples)
            {
                ShowStatus(
                    InfoBarSeverity.Error,
                    IsAccessDenied(result.Error) ? AppResources.Get("PermissionDeniedTitle") : AppResources.Get("LoadFailedTitle"),
                    IsAccessDenied(result.Error) ? AppResources.Get("PermissionDeniedMessage") : AppResources.Get("LoadFailedMessage"));
            }
        }
        catch (Exception exception)
        {
            _viewModel.Issues.Clear();
            ShowStatus(
                InfoBarSeverity.Error,
                IsAccessDenied(exception) ? AppResources.Get("PermissionDeniedTitle") : AppResources.Get("LoadFailedTitle"),
                IsAccessDenied(exception) ? AppResources.Get("PermissionDeniedMessage") : AppResources.Format("SaveFailedMessage", exception.Message));
        }
        finally
        {
            _isLoading = false;
            RefreshViews();
            SelectIssue(VisibleIssues.FirstOrDefault() ?? _viewModel.Issues.FirstOrDefault());
            SetLoadingState(isLoading: false);
        }
    }

    private async Task<bool> TrySaveIssuesAsync()
    {
        if (_isLoading)
        {
            return false;
        }

        try
        {
            await _issueRepository.SaveAsync(_viewModel.Issues);
            return true;
        }
        catch (Exception exception)
        {
            ShowStatus(
                InfoBarSeverity.Error,
                IsAccessDenied(exception) ? AppResources.Get("PermissionDeniedTitle") : AppResources.Get("SaveFailedTitle"),
                IsAccessDenied(exception) ? AppResources.Get("PermissionDeniedMessage") : AppResources.Format("SaveFailedMessage", exception.Message));
            return false;
        }
    }

    private static bool IsAccessDenied(Exception? exception)
    {
        const int AccessDeniedHResult = unchecked((int)0x80070005);
        return exception is UnauthorizedAccessException || exception?.HResult == AccessDeniedHResult;
    }

    private async Task EnsureInitialLoadAsync()
    {
        if (_initialLoadTask is not null)
        {
            await _initialLoadTask;
        }
    }

    private void SetLoadingState(bool isLoading)
    {
        CustomThemeRoot.IsHitTestVisible = !isLoading;
        FluentNativeRoot.IsEnabled = !isLoading;
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        LoadingProgressRing.IsActive = isLoading;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusInfoBar.IsOpen = false;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private void RefreshViews(bool keepSelection = true)
    {
        var filtered = _viewModel.Refresh();

        UpdateMetrics(filtered);
        UpdateEmptyState(filtered.Count == 0);

        if (!keepSelection)
        {
            return;
        }

        if (_selectedIssue is null)
        {
            SelectIssue(filtered.Count > 0 ? filtered[0] : null);
            return;
        }

        if (!filtered.Contains(_selectedIssue))
        {
            SelectIssue(filtered.Count > 0 ? filtered[0] : null);
            return;
        }

        _isRefreshingSelection = true;
        IssueListView.SelectedItem = _selectedIssue;
        FluentIssueListView.SelectedItem = _selectedIssue;
        _isRefreshingSelection = false;
    }

    private void UpdateMetrics(IReadOnlyCollection<IssueItem> filtered)
    {
        var total = filtered.Count;
        var active = filtered.Count(issue => issue.Status is "Todo" or "InProgress");
        var review = filtered.Count(issue => issue.Status == "Review");
        var done = filtered.Count(issue => issue.Status == "Done");
        var completion = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);

        TotalMetricTextBlock.Text = total.ToString(CultureInfo.CurrentCulture);
        ActiveMetricTextBlock.Text = active.ToString(CultureInfo.CurrentCulture);
        ReviewMetricTextBlock.Text = review.ToString(CultureInfo.CurrentCulture);
        CompletionMetricTextBlock.Text = (completion / 100.0).ToString("P0", CultureInfo.CurrentCulture);
        var scopeText = AppResources.Format("ScopeIssueCount", _viewModel.ScopeLabel, filtered.Count);
        ScopeTextBlock.Text = scopeText;
        FluentScopeTextBlock.Text = scopeText;
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
        UpdateNarrowSurfaceVisibility();
    }

    private void SyncNavigationSelection()
    {
        _isSyncingNavigation = true;
        try
        {
            SelectNavigationItemByTag(ShellNavigation, _viewModel.NavigationScope);
            SelectNavigationItemByTag(FluentNativeRoot, _viewModel.NavigationScope);
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
            SelectComboBoxByTag(FluentStatusFilterComboBox, _viewModel.StatusFilter);
            SelectComboBoxByTag(FluentPriorityFilterComboBox, _viewModel.PriorityFilter);
            SelectDropDownByTag(StatusFilterDropDownButton, _viewModel.StatusFilter);
            SelectDropDownByTag(PriorityFilterDropDownButton, _viewModel.PriorityFilter);
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
            if (source != SearchTextBox && SearchTextBox.Text != _viewModel.SearchQuery)
            {
                SearchTextBox.Text = _viewModel.SearchQuery;
            }

            if (source != FluentSearchTextBox && FluentSearchTextBox.Text != _viewModel.SearchQuery)
            {
                FluentSearchTextBox.Text = _viewModel.SearchQuery;
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
        if (!new UISettings().AnimationsEnabled)
        {
            PrepareVisibleSurface(element, show);
            return;
        }

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
        _isShowingNarrowDetail = issue is not null;

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
        IssueDueDatePicker.Date = ParseStorageDate(issue.DueDate);
        IssueLabelsTextBox.Text = issue.Labels;
        DetailHintTextBlock.Text = AppResources.Format("LastSelection", issue.Project, issue.AssigneeDisplay);

        SelectDropDownByTag(DetailStatusDropDownButton, issue.Status);
        SelectDropDownByTag(DetailPriorityDropDownButton, issue.Priority);
        SelectDropDownByTag(DetailProjectDropDownButton, issue.Project);
        SelectDropDownByTag(DetailAssigneeDropDownButton, issue.Assignee);

        FluentSelectedKeyTextBlock.Text = $"{issue.Key} · {issue.StatusLabel}";
        FluentSelectedStatusTextBlock.Text = issue.PriorityLabel;
        FluentIssueTitleTextBox.Text = issue.Title;
        FluentIssueDescriptionTextBox.Text = issue.Description;
        FluentIssueDueDatePicker.Date = ParseStorageDate(issue.DueDate);
        FluentIssueLabelsTextBox.Text = issue.Labels;
        FluentDetailHintTextBlock.Text = AppResources.Format("LastSelection", issue.Project, issue.AssigneeDisplay);

        SelectComboBoxByTag(FluentDetailStatusComboBox, issue.Status);
        SelectComboBoxByTag(FluentDetailPriorityComboBox, issue.Priority);
        SelectComboBoxByTag(FluentDetailProjectComboBox, issue.Project);
        SelectComboBoxByTag(FluentDetailAssigneeComboBox, issue.Assignee);
    }

    private static string NormalizeTitle(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static DateTimeOffset? ParseStorageDate(string value)
    {
        return DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static string FormatStorageDate(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public static Brush StatusToBrush(string status)
    {
        var resourceKey = status switch
        {
            "Todo" => "AccentFillColorDefaultBrush",
            "InProgress" => "SystemFillColorCautionBrush",
            "Review" => "SystemFillColorAttentionBrush",
            "Done" => "SystemFillColorSuccessBrush",
            _ => "TextFillColorSecondaryBrush"
        };

        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource)
            && resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Gray);
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

    private static void SelectComboBoxByTag(ComboBox comboBox, string tag)
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
