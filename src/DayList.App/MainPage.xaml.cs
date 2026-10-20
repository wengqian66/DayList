using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Windows.UI;
using DayList.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace DayList;

public sealed partial class MainPage : Page
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly CultureInfo DisplayCulture = CreateCulture();

    private readonly MonthlyReportService _reports = new(Http);
    private readonly PlanGenerationService _plans = new(Http);
    private readonly ObservableCollection<TaskRow> _rows = new();
    private readonly ObservableCollection<CategoryCardModel> _cards = new();
    private enum MainNav { Today, Completed, Flow }
    private MainNav _nav = MainNav.Today;
    private readonly ObservableCollection<OverdueRow> _overdue = new();
    private readonly CancellationTokenSource _pageCts = new();

    private AppSettings _settings = new();
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _watchTimer;
    private FileSystemWatcher? _watcher;
    private string? _watchedPath;
    private string? _knownHash;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _host;
    private int _suppressCount;
    private int _textFocus;
    private bool _pendingExternal;
    private bool _ready;
    private bool _closed;
    private bool _rebuildingPicker;
    private bool _categoryPromptOpen;
    private bool _pickerDeleteCommit;
    private string _composeCategory = DayTaskCategory.UncategorizedKey;
    private TaskRow? _dragRow;
    private Grid? _pointerRow;
    private TaskRow? _fileDropRow;
    private TaskRow? _dropRow;
    private int _sinkFocusGate;
    private CategoryCardModel? _dropCard;
    private DateTimeOffset _taskMenuAt = DateTimeOffset.MinValue;
    private bool _editingSection;
    private bool _sectionCommitPending;
    private bool _overdueExpanded;
    private CarryOverMemory _carryMemory = new();
    private List<OverdueTaskItem> _pendingOverdue = new();
    private string _overdueFilter = CarryOverOffer.AllLabel;
    private bool _templatesReady;
    private bool _noteMatchesDisk = true;
    private DateTimeOffset? _lastSyncAt;
    private bool _syncError;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _syncClock;
    private int _celebrationToken;
    private int _celebrationHideToken;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _celebrationTimer;
    private readonly ConfettiOverlay _confetti = new();
    private int _statusToken;
    private int _statusDismissToken;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _statusTimer;
    private readonly List<InspirationEntry> _inspirations = new();
    private readonly ObservableCollection<InspirationGlance> _inspirationGlance = new();
    private bool _inspirationDialogOpen;
    private readonly FocusSession _focus = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _focusTimer;
    private DateTime _focusStamp;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _reminderTimer;
    private readonly HashSet<string> _reminderInflight = new(StringComparer.Ordinal);
    private readonly Queue<TaskReminder> _dueReminders = new();
    private bool _showingReminder;
    private int _rebuildDepth;
    private bool _rebuildQueued;
    private bool _suspendRowRebuild;
    private readonly HashSet<DateOnly> _daysWithRecords = new();
    private DateOnly _recordWindowStart;
    private DateOnly _recordWindowEnd;
    private bool _recordWindowReady;
    private Brush? _markedDayBrush;
    private Brush? _clearDayBrush;
    private static readonly Color CalendarMarkDot = Color.FromArgb(255, 0x8C, 0x6A, 0x45);

    public MainPage()
    {
        InitializeComponent();
        CategoryCards.ItemsSource = _cards;
        OverdueList.ItemsSource = _overdue;
        _rows.CollectionChanged += (_, _) =>
        {
            if (_suspendRowRebuild)
                return;
            if (!Suppressed)
                _noteMatchesDisk = false;
            RebuildList();
            UpdateStats();
            RefreshCategoryPicker();
        };
        Loaded += OnLoaded;
        DayCalendar.Loaded += (_, _) => ApplyDayPanelBrush();
        Unloaded += OnUnloaded;
        var settingsKey = new KeyboardAccelerator
        {
            Key = (VirtualKey)188,
            Modifiers = VirtualKeyModifiers.Control
        };
        settingsKey.Invoked += (_, args) =>
        {
            args.Handled = true;
            OpenSettings();
        };
        KeyboardAccelerators.Add(settingsKey);
        // ScrollViewer and plain chrome mark pointer events handled and never
        // take focus, so a task TextBox would keep the caret. Listen anyway.
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPagePointerReleased), true);
    }

    public AppSettings CurrentSettings => _settings.Copy();

    public void AttachHost(MainWindow host) => _host = host;

    public void FocusAddBox()
    {
        if (!_settings.HasVault)
            return;
        AddBox.Focus(FocusState.Programmatic);
    }

    public void ShowShellStatus(string message, bool isError)
    {
        ShowStatus(message, isError ? InfoBarSeverity.Error : InfoBarSeverity.Success);
    }

    private bool Suppressed => _suppressCount > 0;


    private void ApplyDayPanelBrush()
    {
        // Date-number grid only. Header / weekday row stay on the flyout chrome.
        if (FindNamed(DayCalendar, "MonthViewScrollViewer") is not ScrollViewer scroller)
            return;
        if (Application.Current.Resources.TryGetValue("SoftCalendarDayBrush", out var value) && value is Brush brush)
            scroller.Background = brush;
    }

    private static DependencyObject? FindNamed(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.Name == name)
                return child;
            var nested = FindNamed(child, name);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready)
            return;
        _ready = true;
        _watchTimer = DispatcherQueue.CreateTimer();
        _watchTimer.Interval = TimeSpan.FromMilliseconds(220);
        _watchTimer.Tick += (_, _) => ReloadFromDisk();
        _settings = SettingsStore.Load();
        try
        {
            _carryMemory = CarryOverStore.Load();
        }
        catch
        {
            _carryMemory = new CarryOverMemory();
        }

        PinButton.IsChecked = _settings.AlwaysOnTop;
        SyncPinChrome();
        EnsureTemplateButtons();
        ShowMode();
        RefreshCategoryPicker();
        SoftUi.StyleComboBox(CategoryPicker);
        if (Application.Current.Resources.TryGetValue("CategoryPickerItemStyle", out var pickerItemStyle)
            && pickerItemStyle is Style categoryPickerItemStyle)
            CategoryPicker.ItemContainerStyle = categoryPickerItemStyle;
        AddBox.Paste += async (_, e) =>
        {
            e.Handled = true;
            await PasteMultilineAsync();
        };
        if (_settings.HasVault)
            LoadDay();
        else
            UpdateDateChrome();

        _reminderTimer = DispatcherQueue.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(5);
        _reminderTimer.IsRepeating = true;
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();
        CheckReminders();

        _focus.Use(FocusDuration.Normalize(_settings.FocusMinutes, _settings.BreakMinutes));
        _focusTimer = DispatcherQueue.CreateTimer();
        _focusTimer.Interval = TimeSpan.FromMilliseconds(250);
        _focusTimer.IsRepeating = true;
        _focusTimer.Tick += (_, _) => OnFocusTick();
        RenderFocus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _closed = true;
        _pageCts.Cancel();
        _reminderTimer?.Stop();
        _focusTimer?.Stop();
        FocusPopup.Dismiss();
        _syncClock?.Stop();
        HideCelebration();
        _watcher?.Dispose();
        _watcher = null;
    }

    private void ShowMode()
    {
        var ready = _settings.HasVault;
        WelcomePanel.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        WorkBody.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        Composer.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        ExplorerButton.IsEnabled = ready;
        ObsidianButton.IsEnabled = ready;
        if (!ready)
        {
            PathText.Text = "还没有连接库";
            CancelSectionEdit();
            SetSectionChromeVisible(false);
            OverduePanel.Visibility = Visibility.Collapsed;
            NavInspirationCount.Text = "";
            if (SyncCapsule is not null)
                SyncCapsule.Visibility = Visibility.Collapsed;
        }
        else
        {
            RefreshSectionTitleChrome();
            RenderSyncCapsule();
        }
    }

    private void LoadDay()
    {
        HideCelebration();
        PushSuppress();
        try
        {
            ClearExternal();
            StatusBar.IsOpen = false;
            var snapshot = DailyNoteStore.Read(_settings, _date);
            Apply(snapshot);
            ArmWatcher(snapshot.FullPath);
            UpdateDateChrome();
            RefreshOverdue();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            UpdateDateChrome();
        }
        finally
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PopSuppress);
        }
    }

    private void Apply(DailyNoteSnapshot snapshot)
    {
        _suspendRowRebuild = true;
        try
        {
            _rows.Clear();
            foreach (var task in snapshot.Tasks)
                _rows.Add(TaskRow.From(task, _settings.VaultPath));
        }
        finally
        {
            _suspendRowRebuild = false;
        }

        ApplyInspirations(snapshot.Inspirations);
        BindReminders();
        _knownHash = snapshot.ContentHash;
        _noteMatchesDisk = true;
        PathText.Text = FormatNoteSubtitle(snapshot.RelativePath, snapshot.FileExists);
        ToolTipService.SetToolTip(PathText, snapshot.FullPath);
        RefreshSectionTitleChrome();
        AbsorbNoteCategories(snapshot.Tasks.Select(task => task.Category));
        RebuildList();
        UpdateStats();
        RefreshCategoryPicker();
    }

    private void RebuildList()
    {
        // Checkbox materialization and other handlers can re-enter while the
        // ItemsControl is still building containers. A nested Clear/Add leaves
        // the previous card visuals in the panel, so the same category paints
        // once per re-entry. Finish the in-flight rebuild, then run once more.
        if (_rebuildDepth > 0)
        {
            _rebuildQueued = true;
            return;
        }

        _rebuildDepth++;
        try
        {
            do
            {
                _rebuildQueued = false;
                RebuildListCore();
            }
            while (_rebuildQueued);
        }
        finally
        {
            _rebuildDepth--;
        }
    }

    private void RebuildListCore()
    {
        // Drop the source before mutating. ItemsControl handles Reset poorly
        // while a nested ItemsControl is generating: old containers stay and
        // the next Add appends another copy of the same card.
        if (CategoryCards is not null)
            CategoryCards.ItemsSource = null;

        try
        {
            SyncCardTemplate();
            _cards.Clear();
            IEnumerable<TaskRow> source = _rows;
            if (_nav == MainNav.Completed)
                source = _rows.Where(row => row.IsDone);

            // Saved categories with no tasks this day stay in the picker only.
            foreach (var group in CategoryGrouping.ForMainList(source, row => row.Category))
            {
                var card = new CategoryCardModel(group.Key);
                foreach (var row in group.Items)
                    card.Tasks.Add(row);
                card.RefreshStats();
                _cards.Add(card);
            }
        }
        finally
        {
            if (CategoryCards is not null && !ReferenceEquals(CategoryCards.ItemsSource, _cards))
                CategoryCards.ItemsSource = _cards;
            SyncNavChrome();
        }
    }

    private void SyncCardTemplate()
    {
        if (CategoryCards is null)
            return;

        var key = _nav == MainNav.Completed ? "CompletedCardTemplate" : "CategoryCardTemplate";
        if (Resources.TryGetValue(key, out var value)
            && value is DataTemplate template
            && !ReferenceEquals(CategoryCards.ItemTemplate, template))
        {
            CategoryCards.ItemTemplate = template;
        }
    }

    private void RefreshOverdue()
    {
        if (OverduePanel is null)
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!_settings.HasVault || _date != today || _nav != MainNav.Today)
        {
            _overdueExpanded = false;
            OverduePanel.Visibility = Visibility.Collapsed;
            SyncOverdueChrome();
            return;
        }

        IReadOnlyList<OverdueTaskItem> open;
        try
        {
            open = OverdueScan.CollectOpenBefore(_settings, today);
        }
        catch
        {
            open = Array.Empty<OverdueTaskItem>();
        }

        var todayTasks = _rows.Select(row => new TodayTaskRef(row.Text, row.Category)).ToList();
        var absorbed = open.Where(item => CarryOverOffer.AlreadyOnToday(item, todayTasks)).ToList();
        // Only remember tasks that are actually in the note. A failed write must not
        // dismiss the banner across the next launch.
        if (_noteMatchesDisk && absorbed.Count > 0)
            RememberHandled(absorbed);

        _pendingOverdue = CarryOverOffer.Pending(open, todayTasks, _carryMemory, today).ToList();
        if (!string.Equals(_overdueFilter, CarryOverOffer.AllLabel, StringComparison.Ordinal)
            && CarryOverOffer.Filter(_pendingOverdue, _overdueFilter).Count == 0)
        {
            _overdueFilter = CarryOverOffer.AllLabel;
        }

        ApplyOverdueFilter();
    }

    private void ApplyOverdueFilter()
    {
        var visible = CarryOverOffer.Filter(_pendingOverdue, _overdueFilter);
        _overdue.Clear();
        foreach (var item in visible)
            _overdue.Add(new OverdueRow(item));

        if (OverdueFilterLabel is not null)
        {
            var label = string.IsNullOrWhiteSpace(_overdueFilter)
                ? CarryOverOffer.AllLabel
                : _overdueFilter;
            OverdueFilterLabel.Text = label;
            // 「全部」must stay fully visible. Only long category names ellipsize.
            var keepWholeWord = label.Length <= 4;
            OverdueFilterLabel.MaxWidth = keepWholeWord ? double.PositiveInfinity : 120;
            OverdueFilterLabel.TextTrimming = keepWholeWord
                ? TextTrimming.None
                : TextTrimming.CharacterEllipsis;
        }

        if (_pendingOverdue.Count == 0)
        {
            _overdueExpanded = false;
            OverduePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            OverduePanel.Visibility = Visibility.Visible;
        }

        SyncOverdueChrome();
    }

    private void SyncOverdueChrome()
    {
        if (OverdueChevron is null || OverdueTitle is null || OverdueList is null)
            return;

        var expanded = _overdueExpanded && _overdue.Count > 0;
        OverdueChevron.Glyph = expanded ? "\uE70D" : "\uE76C";
        OverdueList.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        OverdueTitle.Text = CarryOverOffer.BannerTitle(
            DateOnly.FromDateTime(DateTime.Now),
            _overdue.Select(row => row.Item.Date).ToList());
    }

    private void OverdueHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, BringTodayButton)
                    || ReferenceEquals(current, OverdueFilterButton)
                    || ReferenceEquals(current, DismissOverdueButton))
                    return;
            }
        }

        if (_pendingOverdue.Count == 0)
            return;
        e.Handled = true;
        _overdueExpanded = !_overdueExpanded;
        SyncOverdueChrome();
    }

    private void OverdueFilter_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var label in OverdueFilterLabels())
        {
            var captured = label;
            var item = new MenuFlyoutItem { Text = captured };
            if (string.Equals(captured, _overdueFilter, StringComparison.Ordinal))
                item.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            item.Click += (_, _) =>
            {
                _overdueFilter = captured;
                ApplyOverdueFilter();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(OverdueFilterButton);
    }

    private IEnumerable<string> OverdueFilterLabels()
    {
        yield return CarryOverOffer.AllLabel;
        foreach (var name in _pendingOverdue
            .Select(item => DayTaskCategory.Display(item.Category))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.CurrentCulture))
        {
            if (!string.Equals(name, CarryOverOffer.AllLabel, StringComparison.Ordinal))
                yield return name;
        }
    }

    private void DismissOverdue_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingOverdue.Count == 0)
            return;
        RememberHandled(_pendingOverdue);
        _overdueExpanded = false;
        RefreshOverdue();
    }

    private void RememberHandled(IEnumerable<OverdueTaskItem> items)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var updated = CarryOverOffer.MarkHandled(_carryMemory, today, items);
        if (updated.SameAs(_carryMemory))
            return;

        _carryMemory = updated;
        try
        {
            CarryOverStore.Save(_carryMemory);
        }
        catch (Exception ex)
        {
            ShowStatus("没能记下已处理的未完成项：" + ex.Message, InfoBarSeverity.Warning);
        }
    }

    private void RefreshCategoryPicker()
    {
        if (CategoryPicker is null)
            return;

        _rebuildingPicker = true;
        try
        {
            CategoryPicker.Items.Clear();
            foreach (var entry in CategoryCatalog.PickerEntries(
                         _settings.TaskCategories,
                         _rows.Select(row => row.Category).Append(_composeCategory)))
            {
                CategoryPicker.Items.Add(new CategoryPickerItem(entry.Label, entry.CanDelete));
            }

            SelectComposeCategoryInPicker();
        }
        finally
        {
            _rebuildingPicker = false;
        }
    }

    private void SelectComposeCategoryInPicker()
    {
        if (CategoryPicker is null || CategoryPicker.Items.Count == 0)
            return;

        var wanted = DayTaskCategory.IsUncategorized(_composeCategory)
            ? DayTaskCategory.UncategorizedLabel
            : _composeCategory;
        var match = CategoryPicker.Items.Cast<object>().FirstOrDefault(item =>
            string.Equals(PickerLabel(item), wanted, StringComparison.Ordinal));

        var rebuilding = _rebuildingPicker;
        _rebuildingPicker = true;
        try
        {
            CategoryPicker.SelectedItem = match ?? CategoryPicker.Items[0];
        }
        finally
        {
            _rebuildingPicker = rebuilding;
        }
    }

    private static string PickerLabel(object? item) =>
        item is CategoryPickerItem option
            ? option.Label
            : item?.ToString() ?? DayTaskCategory.UncategorizedLabel;

    private async void CategoryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuildingPicker || _pickerDeleteCommit || CategoryPicker.SelectedItem is null)
            return;

        var selected = PickerLabel(CategoryPicker.SelectedItem);
        if (selected == DayTaskCategory.NewCategorySentinel)
        {
            _categoryPromptOpen = true;
            try
            {
                var created = await PromptNewCategoryAsync();
                if (created is null)
                    SelectComposeCategoryInPicker();
                else
                {
                    _composeCategory = created;
                    RememberCategory(created);
                    RefreshCategoryPicker();
                }
            }
            finally
            {
                _categoryPromptOpen = false;
            }

            FocusAddBoxAfterCategory();
            return;
        }

        _composeCategory = DayTaskCategory.Normalize(selected);
    }

    private void CategoryPicker_DropDownClosed(object sender, object e)
    {
        if (_categoryPromptOpen || _pickerDeleteCommit)
            return;

        FocusAddBoxAfterCategory();
    }

    private void CategoryPickerItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        void SyncTrash()
        {
            if (element.FindName("PickerDeleteButton") is not Button button)
                return;
            if (button.DataContext is not CategoryPickerItem item || !item.CanDelete)
                return;

            // The closed combo reuses this template. Keep the trash only inside
            // the open drop-down, and undo a pass that ran before the row was
            // parented under a ComboBoxItem.
            var inDropDown = HasAncestor<ComboBoxItem>(element);
            button.Visibility = inDropDown ? Visibility.Visible : Visibility.Collapsed;
            button.IsHitTestVisible = inDropDown;
        }

        SyncTrash();
        element.DispatcherQueue.TryEnqueue(SyncTrash);
        element.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncTrash);
    }

    private void CategoryPickerDelete_Click(object sender, RoutedEventArgs e)
    {
        // Button marks PointerPressed and PointerReleased handled in its class
        // handler, so XAML handlers for those events never run. Click is the
        // event the button actually raises. The combo row selects only on an
        // unhandled pointer release, so this click does not change category.
        // RoutedEventArgs (Button.Click) has no Handled in WinUI.
        if (_pickerDeleteCommit || _closed)
            return;

        var item = FindPickerItem(sender);
        if (!CategoryCatalog.TryAcceptPickerDelete(
                item?.Label,
                item?.CanDelete == true,
                _composeCategory,
                out var categoryToDelete,
                out var composeCategory))
            return;

        _pickerDeleteCommit = true;
        _composeCategory = composeCategory;

        // Close the popup on the next tick. Doing it inside this Click would
        // unload the button while its pointer-release is still on the stack.
        var queued = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            async () =>
            {
                try
                {
                    if (_closed)
                        return;

                    if (CategoryPicker is not null)
                        CategoryPicker.IsDropDownOpen = false;
                    SelectComposeCategoryInPicker();
                    await ConfirmDeleteCategoryAsync(categoryToDelete);
                }
                finally
                {
                    _pickerDeleteCommit = false;
                    if (!_closed)
                        FocusAddBoxAfterCategory();
                }
            });

        if (!queued)
            _pickerDeleteCommit = false;
    }

    private static CategoryPickerItem? FindPickerItem(object sender)
    {
        for (DependencyObject? current = sender as DependencyObject;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: CategoryPickerItem item })
                return item;
        }

        return null;
    }

    private static bool HasAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Category selection is finished. Focus returns after the current key
    /// so the Enter that closed the list does not also add a task.
    /// </summary>
    private void FocusAddBoxAfterCategory()
    {
        void Later()
        {
            if (_closed || _categoryPromptOpen || _pickerDeleteCommit || AddBox is null)
                return;
            AddBox.Focus(FocusState.Programmatic);
            var len = AddBox.Text?.Length ?? 0;
            AddBox.SelectionStart = len;
            AddBox.SelectionLength = 0;
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            Later();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Later);
        });
    }

    private async Task<string?> PromptNewCategoryAsync(string? title = null)
    {
        var box = SoftUi.CreateDialogTextBox("例如：工作、生活", acceptsReturn: false, minHeight: 36, maxHeight: 48);
        box.MaxLength = 40;
        var dialog = SoftUi.CreateDialog(XamlRoot, title ?? "新建分类", box, "确定");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var name = DayTaskCategory.Normalize(box.Text);
        if (DayTaskCategory.IsUncategorized(name))
        {
            ShowStatus("分类名不能为空。", InfoBarSeverity.Warning);
            return null;
        }

        return name;
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (Suppressed || sender is not FrameworkElement element || element.DataContext is not TaskRow row)
            return;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再改分类。", InfoBarSeverity.Warning);
            return;
        }

        var flyout = new MenuFlyout();
        SoftUi.StyleMenuFlyout(flyout);
        void AddItem(string label, string category)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) =>
            {
                row.Category = category;
                RebuildList();
                RefreshCategoryPicker();
                Persist();
            };
            flyout.Items.Add(item);
        }

        AddItem(DayTaskCategory.UncategorizedLabel, DayTaskCategory.UncategorizedKey);
        foreach (var name in KnownCategories())
            AddItem(name, name);

        var create = new MenuFlyoutItem { Text = DayTaskCategory.NewCategorySentinel };
        create.Click += async (_, _) =>
        {
            var created = await PromptNewCategoryAsync();
            if (created is null)
                return;
            row.Category = created;
            RememberCategory(created);
            RebuildList();
            RefreshCategoryPicker();
            Persist();
        };
        flyout.Items.Add(create);

        flyout.ShowAt(element, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
    }

    private IEnumerable<string> KnownCategories() =>
        CategoryCatalog.Available(
            _settings.TaskCategories,
            _rows.Select(row => row.Category).Append(_composeCategory));

    private void RememberCategory(string? name) =>
        RememberCategories(new[] { name });

    private void RememberCategories(IEnumerable<string?> names) =>
        ApplyCatalogUpdate(CategoryCatalog.RememberAll(_settings.TaskCategories, _settings.RetiredTaskCategories, names));

    private void AbsorbNoteCategories(IEnumerable<string?> names) =>
        ApplyCatalogUpdate(CategoryCatalog.Absorb(_settings.TaskCategories, _settings.RetiredTaskCategories, names));

    private void ApplyCatalogUpdate(CategoryCatalogUpdate update)
    {
        if (!update.Changed)
            return;

        _settings.TaskCategories = update.Categories.ToList();
        _settings.RetiredTaskCategories = update.Retired.ToList();
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            ShowStatus("没能保存分类：" + ex.Message, InfoBarSeverity.Warning);
        }
    }

    private async Task ConfirmDeleteCategoryAsync(string? category)
    {
        var name = DayTaskCategory.Normalize(category);
        if (DayTaskCategory.IsUncategorized(name))
            return;

        var count = _rows.Count(row => DayTaskCategory.Normalize(row.Category) == name);
        var body = count == 0
            ? $"从列表移除「{name}」。已写在笔记里的任务不会改动。\nRemoves “{name}” from the list. Tasks already written in notes stay as they are."
            : $"「{name}」今天还有 {count} 条任务，会留在笔记里。只从列表移除。\n“{name}” still has {count} task(s) today. They stay in the note. Only the list entry is removed.";
        var dialog = SoftUi.CreateDialog(
            XamlRoot,
            "删除分类 / Delete category",
            SoftUi.CreateDialogBody(body),
            "删除 / Delete",
            "取消 / Cancel",
            ContentDialogButton.Close);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var update = CategoryCatalog.Forget(_settings.TaskCategories, _settings.RetiredTaskCategories, name);
        ApplyCatalogUpdate(update);
        if (!update.Changed)
        {
            ShowStatus($"「{name}」已经不在分类列表里。 / “{name}” is already off the list.", InfoBarSeverity.Informational);
            return;
        }

        if (DayTaskCategory.Normalize(_composeCategory) == name)
            _composeCategory = DayTaskCategory.UncategorizedKey;

        RebuildList();
        RefreshCategoryPicker();
        ShowStatus(
            count == 0
                ? $"已删除分类「{name}」。 / Deleted “{name}”."
                : $"已从列表移除「{name}」。今天的任务还在。 / Removed “{name}” from the list. Today's tasks stay.",
            InfoBarSeverity.Success);
    }

    private void TaskRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        Windows.Foundation.Point? point = args.TryGetPosition(sender, out var at) ? at : null;
        if (ShowTaskMenuFrom(sender, point))
            args.Handled = true;
    }

    private void TaskRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var point = sender is UIElement element ? e.GetPosition(element) : (Windows.Foundation.Point?)null;
        if (ShowTaskMenuFrom(sender, point))
            e.Handled = true;
    }

    private void TaskRow_Holding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != Microsoft.UI.Input.HoldingState.Started)
            return;
        var point = sender is UIElement element ? e.GetPosition(element) : (Windows.Foundation.Point?)null;
        if (ShowTaskMenuFrom(sender, point))
            e.Handled = true;
    }

    private bool ShowTaskMenuFrom(object sender, Windows.Foundation.Point? point)
    {
        if ((DateTimeOffset.Now - _taskMenuAt).TotalMilliseconds < 350)
            return true;
        if (sender is not FrameworkElement element || element.DataContext is not TaskRow row)
            return false;
        if (Suppressed)
            return false;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再改这条任务。", InfoBarSeverity.Warning);
            return true;
        }

        _taskMenuAt = DateTimeOffset.Now;
        var flyout = new MenuFlyout();
        SoftUi.StyleMenuFlyout(flyout);

        var change = new MenuFlyoutSubItem { Text = "改分类" };
        void AddCategory(string label, string category)
        {
            var item = new MenuFlyoutItem { Text = label };
            if (DayTaskCategory.Normalize(row.Category) == DayTaskCategory.Normalize(category))
                item.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            item.Click += (_, _) => ApplyRowCategory(row, category);
            change.Items.Add(item);
        }

        AddCategory(DayTaskCategory.UncategorizedLabel, DayTaskCategory.UncategorizedKey);
        foreach (var name in KnownCategories())
            AddCategory(name, name);
        var create = new MenuFlyoutItem { Text = DayTaskCategory.NewCategorySentinel };
        create.Click += async (_, _) =>
        {
            var created = await PromptNewCategoryAsync();
            if (created is null)
                return;
            RememberCategory(created);
            ApplyRowCategory(row, created);
        };
        change.Items.Add(create);
        flyout.Items.Add(change);

        var remind = new MenuFlyoutItem { Text = "设提醒" };
        remind.Click += async (_, _) => await PromptReminderAsync(row);
        flyout.Items.Add(remind);

        var attach = new MenuFlyoutItem { Text = "添加附件" };
        attach.Click += async (_, _) => await AddAttachmentsAsync(row);
        flyout.Items.Add(attach);

        var tomorrow = new MenuFlyoutItem { Text = "挪到明天" };
        tomorrow.Click += (_, _) => MoveTaskToNextDay(row);
        flyout.Items.Add(tomorrow);

        var inspire = new MenuFlyoutItem { Text = "转灵感" };
        inspire.Click += (_, _) => ConvertTaskToInspiration(row);
        flyout.Items.Add(inspire);

        var remove = new MenuFlyoutItem { Text = "删除" };
        remove.Click += (_, _) => DeleteRow(row);
        flyout.Items.Add(remove);

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(new MenuFlyoutItem { Text = "取消" });

        var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        if (point is Windows.Foundation.Point at)
            options.Position = at;
        flyout.ShowAt(element, options);
        return true;
    }

    private void ApplyRowCategory(TaskRow row, string category)
    {
        row.Category = category;
        RebuildList();
        RefreshCategoryPicker();
        Persist();
    }

    private void MoveTaskToNextDay(TaskRow row)
    {
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再挪动任务。", InfoBarSeverity.Warning);
            return;
        }

        var text = (row.Text ?? "").Trim();
        if (text.Length == 0)
        {
            ShowStatus("先写上内容，再挪到明天。", InfoBarSeverity.Warning);
            return;
        }

        var index = _rows.IndexOf(row);
        if (index < 0)
            return;

        var next = _date.AddDays(1);
        DailyNoteSnapshot snapshot;
        try
        {
            snapshot = DailyNoteStore.Read(_settings, next);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        var move = TaskReorder.MoveToOtherDay(CurrentTasks(), index, snapshot.Tasks);
        try
        {
            DailyNoteStore.Write(_settings, next, move.Target);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        var reminderId = row.ReminderId;
        var reminderTime = row.ReminderTime;
        _rows.Remove(row);
        if (!Persist())
        {
            try
            {
                DailyNoteStore.Write(_settings, next, snapshot.Tasks);
            }
            catch (Exception ex)
            {
                ShowStatus("已写到下一天，但今天这份没能更新：" + ex.Message, InfoBarSeverity.Error);
                return;
            }

            InsertRow(row);
            ShowStatus("没能从这一天移走这条任务。", InfoBarSeverity.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(reminderId) && ReminderSchedule.TryParseClock(reminderTime, out var time))
        {
            try
            {
                ReminderStore.SetForTask(ReminderStore.DefaultPath, reminderId, text, next, time);
            }
            catch (Exception ex)
            {
                ShowStatus("任务已挪走，提醒没能一起改：" + ex.Message, InfoBarSeverity.Warning);
                return;
            }
        }

        ShowStatus("已挪到" + WhenLabel(next) + "。", InfoBarSeverity.Success);
    }

    private void ConvertTaskToInspiration(TaskRow row)
    {
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再转灵感。", InfoBarSeverity.Warning);
            return;
        }

        if (InspirationSection.SameHeading(_settings.SectionHeading, _settings.InspirationHeading))
        {
            ShowStatus("灵感小节不能和待办小节同名。到设置里改一个标题。", InfoBarSeverity.Warning);
            return;
        }

        var text = (row.Text ?? "").Trim();
        if (text.Length == 0)
        {
            ShowStatus("先写上内容，再转成灵感。", InfoBarSeverity.Warning);
            return;
        }

        var paths = ManagedPaths(row);
        var index = _rows.IndexOf(row);
        if (index < 0)
            return;

        var (_, entry) = TaskReorder.ExtractInspiration(
            CurrentTasks(),
            index,
            DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
        var previous = _inspirations
            .Select(item => new InspirationEntry
            {
                TimeLabel = item.TimeLabel,
                Text = item.Text,
                RawLine = item.RawLine
            })
            .ToList();
        var nextEntries = previous.Append(entry).ToList();

        WriteResult written;
        try
        {
            written = DailyNoteStore.WriteInspirations(_settings, _date, nextEntries);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        _knownHash = written.ContentHash;
        ArmWatcher(written.FullPath);
        _rows.Remove(row);
        if (!Persist())
        {
            try
            {
                var rollback = DailyNoteStore.WriteInspirations(_settings, _date, previous);
                _knownHash = rollback.ContentHash;
                ArmWatcher(rollback.FullPath);
            }
            catch (Exception ex)
            {
                ApplyInspirations(nextEntries);
                ShowStatus("灵感已写入，但任务还在：" + ex.Message, InfoBarSeverity.Warning);
                return;
            }

            InsertRow(row);
            ApplyInspirations(previous);
            ShowStatus("没能把这条从待办里移走。", InfoBarSeverity.Warning);
            return;
        }

        ForgetReminder(row);
        ApplyInspirations(nextEntries);
        ReleaseManagedFiles(paths);
        ShowStatus("已转为灵感。", InfoBarSeverity.Success);
    }

    private void UpdateDateChrome()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dt = _date.ToDateTime(TimeOnly.MinValue);
        DateLabel.Text = dt.ToString("M月d日 ddd", DisplayCulture);
        SideDateText.Text = dt.ToString("M月d日 ddd", DisplayCulture);
        if (SideWeekText is not null)
            SideWeekText.Visibility = Visibility.Collapsed;
        TodayButton.Visibility = _date == today ? Visibility.Collapsed : Visibility.Visible;
        SyncCalendar();
    }

    private void SyncCalendar()
    {
        PushSuppress();
        try
        {
            var local = new DateTimeOffset(_date.ToDateTime(TimeOnly.MinValue));
            DayCalendar.SelectedDates.Clear();
            DayCalendar.SelectedDates.Add(local);
            DayCalendar.SetDisplayDate(local);
        }
        finally
        {
            PopSuppress();
        }
    }

    private void DateFlyout_Opened(object sender, object e)
    {
        _recordWindowReady = false;
        ApplyRecordMarks(DayCalendar);
    }

    private void DayCalendar_DayItemChanging(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
    {
        if (args.Phase == 0)
        {
            args.RegisterUpdateCallback(DayCalendar_DayItemChanging);
            return;
        }

        PaintCalendarDay(args.Item);
    }

    private void ApplyRecordMarks(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CalendarViewDayItem day)
                PaintCalendarDay(day);
            else
                ApplyRecordMarks(child);
        }
    }

    private void PaintCalendarDay(CalendarViewDayItem item)
    {
        var date = DateOnly.FromDateTime(item.Date.LocalDateTime.Date);
        EnsureRecordMarks(date);
        var marked = _daysWithRecords.Contains(date);
        item.SetDensityColors(marked ? new[] { CalendarMarkDot } : Array.Empty<Color>());
        item.Background = marked ? MarkedDayBrush() : ClearDayBrush();
    }

    private void EnsureRecordMarks(DateOnly day)
    {
        if (_recordWindowReady && day >= _recordWindowStart && day <= _recordWindowEnd)
            return;

        var firstOfMonth = new DateOnly(day.Year, day.Month, 1);
        _recordWindowStart = firstOfMonth.AddDays(-7);
        _recordWindowEnd = firstOfMonth.AddMonths(1).AddDays(6);
        _daysWithRecords.Clear();
        foreach (var marked in DayRecordIndex.WithRecords(_settings, _recordWindowStart, _recordWindowEnd))
            _daysWithRecords.Add(marked);
        _recordWindowReady = true;
    }

    private Brush MarkedDayBrush()
    {
        if (_markedDayBrush is not null)
            return _markedDayBrush;
        if (Application.Current.Resources.TryGetValue("SoftCalendarMarkedBrush", out var value) && value is Brush brush)
            _markedDayBrush = brush;
        else
            _markedDayBrush = new SolidColorBrush(Color.FromArgb(255, 0xE4, 0xCD, 0xB4));
        return _markedDayBrush;
    }

    private Brush ClearDayBrush() => _clearDayBrush ??= new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    private void UpdateStats()
    {
        var total = _rows.Count;
        var done = _rows.Count(row => row.IsDone);
        var open = total - done;
        StatsText.Text = total == 0
            ? "0 / 0 完成"
            : $"{done} / {total} 完成";

        SideProgressRing.Maximum = 1;
        SideProgressRing.Value = total == 0 ? 0 : (double)done / total;
        SideProgressPercent.Text = total == 0 ? "0%" : $"{(int)Math.Round(100.0 * done / total)}%";

        NavTodayCount.Text = total == 0 ? "" : total.ToString();
        NavDoneCount.Text = done == 0 ? "" : done.ToString();

        foreach (var card in _cards)
            card.RefreshStats();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var showTemplates = _nav == MainNav.Today && total == 0 && _settings.HasVault;
        if (_nav == MainNav.Completed)
        {
            EmptyHint.Text = done == 0 ? "还没有已完成的任务" : "";
            EmptyState.Visibility = done == 0 ? Visibility.Visible : Visibility.Collapsed;
            TemplateLead.Visibility = Visibility.Collapsed;
            TemplateHost.Visibility = Visibility.Collapsed;
            GreetingText.Text = done == 0 ? "完成任务后会出现在这里。" : "这些是已经勾掉的任务。";
        }
        else if (_nav == MainNav.Flow)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            TemplateLead.Visibility = Visibility.Collapsed;
            TemplateHost.Visibility = Visibility.Collapsed;
            GreetingText.Text = "近几天的灵感。";
        }
        else
        {
            EmptyHint.Text = _date == today
                ? "今天还没有任务"
                : "这一天还没有任务";
            EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            TemplateLead.Visibility = showTemplates ? Visibility.Visible : Visibility.Collapsed;
            TemplateHost.Visibility = showTemplates ? Visibility.Visible : Visibility.Collapsed;
            GreetingText.Text = total == 0
                ? "今天也是充实的一天。"
                : open == 0
                    ? "今天的任务都完成了。"
                    : "今天也是充实的一天。";
        }

        SyncNavChrome();
    }

    private void SetDate(DateOnly date)
    {
        if (Suppressed)
            return;
        if (date == _date)
        {
            UpdateDateChrome();
            return;
        }

        CommitFocusedText();
        _pendingExternal = false;
        ExternalBar.IsOpen = false;
        Persist();
        _date = date;
        LoadDay();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => SetDate(_date.AddDays(-1));

    private void Next_Click(object sender, RoutedEventArgs e) => SetDate(_date.AddDays(1));

    private void Today_Click(object sender, RoutedEventArgs e) => SetDate(DateOnly.FromDateTime(DateTime.Now));

    private void DayCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (Suppressed || DayCalendar.SelectedDates.Count == 0)
            return;
        var picked = DateOnly.FromDateTime(DayCalendar.SelectedDates[0].LocalDateTime.Date);
        SetDate(picked);
        DateButton.Flyout?.Hide();
    }


    private void NavToday_Click(object sender, RoutedEventArgs e)
    {
        if (_nav == MainNav.Today)
            return;
        _nav = MainNav.Today;
        RebuildList();
        UpdateStats();
        RefreshOverdue();
    }

    private void NavDone_Click(object sender, RoutedEventArgs e)
    {
        if (_nav == MainNav.Completed)
            return;
        _nav = MainNav.Completed;
        RebuildList();
        UpdateStats();
        RefreshOverdue();
    }

    private void NavFlow_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.HasVault)
        {
            ShowStatus("先连接 Obsidian 库，灵感才会写进当天笔记。", InfoBarSeverity.Warning);
            return;
        }

        if (InspirationSection.SameHeading(_settings.SectionHeading, _settings.InspirationHeading))
        {
            ShowStatus("灵感小节和待办小节重名了，请在设置里改一个。", InfoBarSeverity.Warning, "打开设置", () => OpenSettings());
            return;
        }

        _nav = MainNav.Flow;
        RefreshInspirationFlow();
        RebuildList();
        UpdateStats();
        RefreshOverdue();
    }

    private void SyncNavChrome()
    {
        if (NavTodayButton is null || NavDoneButton is null)
            return;

        var selected = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftNavSelectedBrush"];
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var flow = _nav == MainNav.Flow;
        NavTodayButton.Background = _nav == MainNav.Today ? selected : clear;
        NavDoneButton.Background = _nav == MainNav.Completed ? selected : clear;
        if (NavFlowButton is not null)
            NavFlowButton.Background = flow ? selected : clear;
        if (InspirationStrip is not null)
            InspirationStrip.Visibility = _nav == MainNav.Today ? Visibility.Visible : Visibility.Collapsed;
        if (InspirationFlowPanel is not null)
            InspirationFlowPanel.Visibility = flow ? Visibility.Visible : Visibility.Collapsed;
        if (CategoryCards is not null)
            CategoryCards.Visibility = flow ? Visibility.Collapsed : Visibility.Visible;
        if (flow && EmptyState is not null)
            EmptyState.Visibility = Visibility.Collapsed;
        if (Composer is not null && _settings.HasVault)
            Composer.Visibility = flow ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NewTaskInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_settings.HasVault)
        {
            OpenSettings();
            return;
        }

        if (_nav == MainNav.Flow)
        {
            _nav = MainNav.Today;
            RebuildList();
            UpdateStats();
            RefreshOverdue();
        }

        AddBox.Focus(FocusState.Programmatic);
    }

    private async void PasteTasksInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is not TextBox box || !ReferenceEquals(box, AddBox))
            return;
        args.Handled = true;
        await PasteMultilineAsync();
    }

    private async Task PasteMultilineAsync()
    {
        if (!_settings.HasVault || Suppressed || _pendingExternal)
            return;

        string text;
        try
        {
            var package = Clipboard.GetContent();
            if (!package.Contains(StandardDataFormats.Text))
                return;
            text = await package.GetTextAsync();
        }
        catch
        {
            return;
        }

        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (lines.Count == 0)
            return;

        if (lines.Count == 1)
        {
            var existing = AddBox.Text ?? "";
            AddBox.Text = existing.Length == 0 ? lines[0] : existing + lines[0];
            AddBox.SelectionStart = AddBox.Text.Length;
            return;
        }

        foreach (var line in lines)
        {
            InsertRow(new TaskRow
            {
                Text = line,
                Category = _composeCategory
            });
        }

        AddBox.Text = "";
        Persist();
        UpdateStats();
        KeepAddFocus();
        ShowStatus($"已添加 {lines.Count} 条任务。", InfoBarSeverity.Success);
    }

    private void AddBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;
        e.Handled = true;
        AddTask();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddTask();

    private void AddTask()
    {
        if (!_settings.HasVault || Suppressed)
            return;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再添加任务。", InfoBarSeverity.Warning);
            return;
        }

        var text = (AddBox.Text ?? "").Trim();
        if (text.Length == 0)
            return;

        var parsed = QuickAddPhrase.Parse(text, DateOnly.FromDateTime(DateTime.Now), KnownCategories());
        var title = parsed.Title.Trim();
        if (title.Length == 0)
            return;

        var category = parsed.Recognized && parsed.Category is not null
            ? parsed.Category
            : _composeCategory;
        if (parsed.Recognized && !DayTaskCategory.IsUncategorized(parsed.Category))
            RememberCategory(parsed.Category);
        var date = parsed.Recognized && parsed.Date is DateOnly day ? day : _date;

        if (date != _date)
        {
            if (!TryAppendOnDate(date, title, category))
                return;
            AddBox.Text = "";
            string? savedTime = null;
            var reminded = parsed.Reminder is TimeOnly clock
                && TrySaveReminder(null, title, date, clock, out savedTime);
            ShowStatus(QuickAddStatus(title, date, category, parsed, reminded, savedTime), InfoBarSeverity.Success);
            KeepAddFocus();
            return;
        }

        var row = new TaskRow
        {
            Text = title,
            Category = category
        };
        InsertRow(row);
        AddBox.Text = "";
        Persist();
        string? localSaved = null;
        var remindedLocal = parsed.Recognized
            && parsed.Reminder is TimeOnly localClock
            && TrySaveReminder(row, title, date, localClock, out localSaved);
        if (parsed.Recognized)
            ShowStatus(QuickAddStatus(title, date, category, parsed, remindedLocal, localSaved), InfoBarSeverity.Success);
        KeepAddFocus(row);
    }

    private bool TryAppendOnDate(DateOnly date, string title, string category)
    {
        try
        {
            var snapshot = DailyNoteStore.Read(_settings, date);
            var tasks = snapshot.Tasks.Select(task => task.Copy()).ToList();
            tasks.Add(new DayTask
            {
                Text = title,
                Category = DayTaskCategory.Normalize(category)
            });
            DailyNoteStore.Write(_settings, date, tasks);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return false;
        }
    }

    private bool TrySaveReminder(TaskRow? row, string title, DateOnly date, TimeOnly time, out string? clock)
    {
        clock = null;
        if (date.ToDateTime(time) <= DateTime.Now)
            return false;

        try
        {
            var saved = ReminderStore.SetForTask(
                ReminderStore.DefaultPath,
                row?.ReminderId,
                title,
                date,
                time);
            clock = saved.Time;
            if (row is not null && date == _date)
            {
                row.ReminderId = saved.Id;
                row.ReminderTime = saved.Time;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowStatus("没能保存提醒：" + ex.Message, InfoBarSeverity.Warning);
            return false;
        }
    }

    private string QuickAddStatus(string title, DateOnly date, string category, QuickAddParse parsed, bool reminded, string? clock)
    {
        var parts = new List<string> { date == _date ? "已添加" : "已加到" + WhenLabel(date) };
        if (reminded && clock is not null)
            parts.Add(clock + " 提醒");
        else if (parsed.Reminder is not null)
            parts.Add("时间已过，未设提醒");
        if (parsed.Category is not null)
            parts.Add("分类「" + DayTaskCategory.Display(category) + "」");
        return string.Join("，", parts) + "：" + title;
    }

    private string WhenLabel(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (date == today)
            return "今天";
        if (date == today.AddDays(1))
            return "明天";
        if (date == today.AddDays(2))
            return "后天";
        return date.ToString("M月d日", DisplayCulture);
    }

    private void KeepAddFocus(TaskRow? scrollTo = null)
    {
        void FocusNow()
        {
            if (_closed)
                return;
            AddBox.Focus(FocusState.Programmatic);
            var len = AddBox.Text?.Length ?? 0;
            AddBox.SelectionStart = len;
            AddBox.SelectionLength = 0;
        }

        FocusNow();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (_closed)
                return;
            FocusNow();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusNow);
        });
    }

    private void InsertRow(TaskRow row, int? at = null)
    {
        if (at is int index)
        {
            index = Math.Clamp(index, 0, _rows.Count);
            _rows.Insert(index, row);
            return;
        }

        var key = DayTaskCategory.Normalize(row.Category);
        var insertAt = _rows.Count;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (DayTaskCategory.Normalize(_rows[i].Category) != key)
                continue;
            insertAt = i + 1;
            while (insertAt < _rows.Count
                   && DayTaskCategory.Normalize(_rows[insertAt].Category) == key)
            {
                insertAt++;
            }

            break;
        }

        if (insertAt >= _rows.Count)
            _rows.Add(row);
        else
            _rows.Insert(insertAt, row);
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (Suppressed || sender is not FrameworkElement element || element.DataContext is not TaskRow row)
            return;
        await AddAttachmentsAsync(row);
    }

    private async Task AddAttachmentsAsync(TaskRow row)
    {
        if (!PrepareAttachmentChange())
            return;

        IReadOnlyList<string> paths;
        try
        {
            paths = await FileBrowser.PickAttachmentsAsync(App.Window);
        }
        catch (Exception ex)
        {
            ShowStatus("无法打开文件选择器。" + ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (_closed || paths.Count == 0)
            return;

        CommitAttachmentPaths(row, paths);
    }

    private bool PrepareAttachmentChange()
    {
        if (_closed || Suppressed)
            return false;
        if (!_settings.HasVault)
        {
            ShowStatus("先连接 Obsidian 库，附件才会复制进库。", InfoBarSeverity.Warning, "选择库", () => PickVault_Click(this, new RoutedEventArgs()));
            return false;
        }

        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再添加附件。", InfoBarSeverity.Warning);
            return false;
        }

        return true;
    }

    private void CommitAttachmentPaths(TaskRow row, IReadOnlyList<string> paths)
    {
        if (_closed || paths.Count == 0)
            return;
        if (!PrepareAttachmentChange())
            return;

        var batch = AttachmentVault.ImportMany(
            _settings.VaultPath,
            _date,
            paths,
            row.Attachments.Select(item => item.RelativePath));
        foreach (var imported in batch.Added)
            row.AddAttachment(imported, _settings.VaultPath);

        if (batch.Added.Count > 0)
            Persist();

        var added = batch.Added.Count;
        if (batch.Error is not null)
        {
            var suffix = added > 0 ? " 已添加 " + added.ToString(CultureInfo.InvariantCulture) + " 个。" : "";
            ShowStatus(batch.Error + suffix, InfoBarSeverity.Error);
            return;
        }

        if (added == 1)
            ShowStatus("已添加附件。", InfoBarSeverity.Success);
        else if (added > 1)
            ShowStatus("已添加 " + added.ToString(CultureInfo.InvariantCulture) + " 个附件。", InfoBarSeverity.Success);
    }

    private async Task DropAttachmentsAsync(TaskRow row, DataPackageView view)
    {
        if (!PrepareAttachmentChange())
            return;

        IReadOnlyList<IStorageItem> items;
        try
        {
            items = await view.GetStorageItemsAsync();
        }
        catch (Exception ex)
        {
            ShowStatus("无法读取拖入的文件。" + ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (_closed)
            return;

        if (items.Count == 0)
        {
            ShowStatus("没有读到可以附加的文件。", InfoBarSeverity.Error);
            return;
        }

        var paths = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (item is StorageFile file && !string.IsNullOrWhiteSpace(file.Path))
                paths.Add(file.Path);
            else if (!string.IsNullOrWhiteSpace(item.Path))
                paths.Add(item.Path);
            else
                paths.Add("");
        }

        CommitAttachmentPaths(row, paths);
    }

    private async void AttachmentOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Suppressed || sender is not FrameworkElement element || element.DataContext is not TaskAttachmentRow chip)
            return;

        if (string.IsNullOrWhiteSpace(chip.FullPath) || !File.Exists(chip.FullPath))
        {
            ShowStatus("附件不在库里了。", InfoBarSeverity.Warning);
            return;
        }

        if (!chip.IsImage)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = chip.FullPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowStatus("无法打开这个附件。" + ex.Message, InfoBarSeverity.Error);
            }

            return;
        }

        var source = await AttachmentImages.LoadAsync(chip.FullPath, 1600);
        if (source is null)
        {
            ShowStatus("这张图暂时预览不了。", InfoBarSeverity.Warning);
            return;
        }

        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            MaxWidth = 720,
            MaxHeight = 480
        };
        var scroll = new ScrollViewer
        {
            Content = image,
            MaxWidth = 740,
            MaxHeight = 500,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var dialog = SoftUi.CreateDialog(
            XamlRoot,
            chip.DisplayName,
            scroll,
            "关闭",
            "",
            ContentDialogButton.Primary);
        await dialog.ShowAsync();
    }

    private void AttachmentRemove_Click(object sender, RoutedEventArgs e)
    {
        if (Suppressed || sender is not FrameworkElement element || element.DataContext is not TaskAttachmentRow chip)
            return;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再移除附件。", InfoBarSeverity.Warning);
            return;
        }

        var row = OwningTaskRow(element);
        if (row is null)
            return;

        row.RemoveAttachment(chip);
        if (Persist())
            ReleaseManagedFiles(new[] { chip.RelativePath });
    }

    private static TaskRow? OwningTaskRow(DependencyObject? start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is TaskRow row)
                return row;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IReadOnlyList<string> ManagedPaths(TaskRow row) =>
        row.Attachments
            .Select(item => item.RelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

    private void ReleaseManagedFiles(IEnumerable<string> relativePaths)
    {
        if (!_settings.HasVault)
            return;

        foreach (var path in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            AttachmentVault.TryDeleteIfUnreferenced(_settings.VaultPath, path);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskRow row)
            return;
        DeleteRow(row);
    }

    private void DeleteRow(TaskRow row)
    {
        if (Suppressed)
            return;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再删除任务。", InfoBarSeverity.Warning);
            return;
        }

        var paths = ManagedPaths(row);
        ForgetReminder(row);
        _rows.Remove(row);
        if (Persist())
            ReleaseManagedFiles(paths);
    }

    private void TaskDoneChanged(object sender, RoutedEventArgs e)
    {
        if (Suppressed || sender is not CheckBox box || box.DataContext is not TaskRow row)
            return;

        var done = box.IsChecked == true;
        // Opening 已完成 builds one card, then each already-checked box raises
        // Checked as the template binds IsChecked. Rebuilding there used to run
        // once per done task and the ItemsControl appended a copy each time
        // (two done tasks → the same category card three times).
        if (!TaskCheckInteraction.DoneFlagChanged(row.IsDone, done))
            return;

        var before = DoneSnapshots();
        row.IsDone = done;
        if (_nav == MainNav.Completed)
        {
            // After this handler returns. Rebuilding now would detach the
            // checkbox that is still raising Checked/Unchecked.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RebuildList);
        }

        UpdateStats();
        if (done)
            PlayCheckPop(box);
        if (_pendingExternal)
            return;
        Persist();
        if (done)
            PlayMoment(CompletionFeedback.Detect(before, DoneSnapshots()));
    }

    private void TaskText_GotFocus(object sender, RoutedEventArgs e) => _textFocus++;

    private void TaskText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext is TaskRow row)
            row.Text = box.Text ?? "";
        _textFocus = Math.Max(0, _textFocus - 1);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FinishTextEdit(sender));
    }

    private void TaskText_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox || (sender as FrameworkElement)?.DataContext is not TaskRow row)
            return;
        if (e.Key != VirtualKey.Tab)
            return;

        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        e.Handled = true;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再调整缩进。", InfoBarSeverity.Warning);
            return;
        }

        if (shift)
            row.IndentOut();
        else
            row.IndentIn();
        Persist();
    }

    private void TaskText_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;
        e.Handled = true;
        ReleaseTaskEditorFocus();
    }

    private void OnPagePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_closed || _dragRow is not null)
            return;
        if (FocusManager.GetFocusedElement(XamlRoot) is not TextBox box || box.DataContext is not TaskRow)
            return;
        if (e.OriginalSource is DependencyObject source && IsUnder(box, source))
            return;

        ReleaseTaskEditorFocus();
    }

    /// <summary>
    /// Move focus off a task TextBox. Clicking the page background, sidebar,
    /// or other non-task chrome does not focus those elements, so the caret
    /// would otherwise stay in the editor until another task TextBox is clicked.
    /// </summary>
    private void ReleaseTaskEditorFocus()
    {
        if (_closed || EditorFocusSink is null)
            return;
        if (FocusManager.GetFocusedElement(XamlRoot) is not TextBox box || box.DataContext is not TaskRow)
            return;

        // Leave the sink focusable so the caret does not jump back to the task.
        // Keyboard tab onto it is redirected in EditorFocusSink_GettingFocus.
        _sinkFocusGate++;
        try
        {
            EditorFocusSink.IsTabStop = true;
            EditorFocusSink.UseSystemFocusVisuals = false;
            EditorFocusSink.Focus(FocusState.Programmatic);
        }
        finally
        {
            _sinkFocusGate--;
        }
    }

    private void EditorFocusSink_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (_sinkFocusGate > 0 || args.FocusState != FocusState.Keyboard)
            return;

        EditorFocusSink.IsTabStop = false;
        var direction = args.Direction == FocusNavigationDirection.Previous
            ? FocusNavigationDirection.Previous
            : FocusNavigationDirection.Next;
        DependencyObject? next;
        try
        {
            next = FocusManager.FindNextElement(direction);
        }
        finally
        {
            EditorFocusSink.IsTabStop = true;
        }

        if (next is null || ReferenceEquals(next, sender))
        {
            args.Cancel = true;
            args.Handled = true;
            return;
        }

        args.NewFocusedElement = next;
        args.Handled = true;
    }

    private static bool IsUnder(DependencyObject ancestor, DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private void FinishTextEdit(object sender)
    {
        if (_closed || _textFocus > 0 || Suppressed || _pendingExternal)
            return;
        if (sender is TextBox box && box.DataContext is TaskRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Text) && row.Attachments.Count == 0)
            {
                ForgetReminder(row);
                _rows.Remove(row);
                Persist();
                return;
            }

            SyncReminderText(row);
        }

        Persist();
    }

    private void DragHandle_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: TaskRow row })
        {
            args.Cancel = true;
            return;
        }

        if (_pendingExternal)
        {
            args.Cancel = true;
            ShowStatus("先处理笔记的外部更新，再拖拽。", InfoBarSeverity.Warning);
            return;
        }

        _dragRow = row;
        sender.DropCompleted -= DragHandle_DropCompleted;
        sender.DropCompleted += DragHandle_DropCompleted;
        args.AllowedOperations = DataPackageOperation.Move;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.Data.SetText("daylist-task");
    }

    private void DragHandle_DropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        _dragRow = null;
        ClearDropHighlight();
    }

    private void TaskList_DragOver(object sender, DragEventArgs e)
    {
        if (_dragRow is null)
            return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = "调整顺序";
    }

    private void TaskRow_DragOver(object sender, DragEventArgs e)
    {
        if (_dragRow is null)
        {
            if (!HasStorageItems(e.DataView) || sender is not FrameworkElement { DataContext: TaskRow row })
                return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.Caption = "添加附件";
            e.Handled = true;
            _fileDropRow = row;
            HighlightRow(row);
            return;
        }

        if (sender is not FrameworkElement { DataContext: TaskRow target })
            return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        var same = DayTaskCategory.Normalize(_dragRow.Category) == DayTaskCategory.Normalize(target.Category);
        e.DragUIOverride.Caption = same ? "调整顺序" : "移到此分类";
        e.Handled = true;
        HighlightRow(ReferenceEquals(_dragRow, target) ? null : target);
        HighlightCard(same ? null : CardFor(target));
    }

    private void TaskRow_DragLeave(object sender, DragEventArgs e)
    {
        if (_dragRow is not null)
            return;
        if (sender is not FrameworkElement element || element.DataContext is not TaskRow row)
            return;
        if (!ReferenceEquals(_fileDropRow, row))
            return;

        try
        {
            var point = e.GetPosition(element);
            if (point.X >= 0 && point.Y >= 0 && point.X < element.ActualWidth && point.Y < element.ActualHeight)
                return;
        }
        catch (Exception)
        {
            return;
        }

        _fileDropRow = null;
        if (ReferenceEquals(_dropRow, row))
            HighlightRow(null);
    }

    private static bool HasStorageItems(DataPackageView view)
    {
        try
        {
            return view.Contains(StandardDataFormats.StorageItems);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Header_DragOver(object sender, DragEventArgs e)
    {
        if (_dragRow is null || sender is not FrameworkElement { DataContext: CategoryCardModel card })
            return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = "移到此分类";
        e.Handled = true;
        HighlightRow(null);
        HighlightCard(card);
    }

    private async void TaskRow_Drop(object sender, DragEventArgs e)
    {
        if (_dragRow is null)
        {
            if (sender is not FrameworkElement { DataContext: TaskRow row } || !HasStorageItems(e.DataView))
            {
                ClearDropHighlight();
                return;
            }

            e.Handled = true;
            _fileDropRow = null;
            ClearDropHighlight();
            var deferral = e.GetDeferral();
            try
            {
                await DropAttachmentsAsync(row, e.DataView);
            }
            catch (Exception ex)
            {
                ShowStatus("没有添加附件。" + ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                deferral.Complete();
            }

            return;
        }

        if (sender is not FrameworkElement { DataContext: TaskRow target } || ReferenceEquals(_dragRow, target))
        {
            ClearDropHighlight();
            return;
        }

        e.Handled = true;
        var source = _dragRow;
        _dragRow = null;
        ClearDropHighlight();
        var from = _rows.IndexOf(source);
        var before = _rows.IndexOf(target);
        if (from < 0 || before < 0)
            return;
        ApplyReorder(TaskReorder.MoveBefore(CurrentTasks(), from, before, target.Category));
    }

    private void Header_Drop(object sender, DragEventArgs e)
    {
        if (_dragRow is null || sender is not FrameworkElement { DataContext: CategoryCardModel card })
        {
            ClearDropHighlight();
            return;
        }

        e.Handled = true;
        var source = _dragRow;
        _dragRow = null;
        ClearDropHighlight();
        var from = _rows.IndexOf(source);
        if (from < 0)
            return;
        ApplyReorder(TaskReorder.MoveToCategoryEnd(CurrentTasks(), from, card.Category));
    }

    private void TaskList_Drop(object sender, DragEventArgs e)
    {
        if (_dragRow is null)
            return;
        e.Handled = true;
        var source = _dragRow;
        _dragRow = null;
        ClearDropHighlight();
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再拖拽。", InfoBarSeverity.Warning);
            return;
        }

        if (sender is FrameworkElement { DataContext: CategoryCardModel card })
        {
            var from = _rows.IndexOf(source);
            if (from < 0)
                return;
            ApplyReorder(TaskReorder.MoveToCategoryEnd(CurrentTasks(), from, card.Category));
        }
    }

    private void ApplyReorder(ReorderResult result)
    {
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再拖拽。", InfoBarSeverity.Warning);
            return;
        }

        if (result.SourceIndex.Count != _rows.Count)
            return;

        var rows = result.SourceIndex.Select(index => _rows[index]).ToList();
        var changed = false;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!ReferenceEquals(rows[i], _rows[i]))
                changed = true;
            var category = result.Tasks[i].Category;
            if (!string.Equals(rows[i].Category, category, StringComparison.Ordinal))
            {
                rows[i].Category = category;
                changed = true;
            }
        }

        if (!changed)
            return;

        _suspendRowRebuild = true;
        try
        {
            _rows.Clear();
            foreach (var row in rows)
                _rows.Add(row);
        }
        finally
        {
            _suspendRowRebuild = false;
        }

        RebuildList();
        RefreshCategoryPicker();
        UpdateStats();
        Persist();
    }

    private CategoryCardModel? CardFor(TaskRow row) =>
        _cards.FirstOrDefault(card => card.Tasks.Contains(row));

    private void HighlightRow(TaskRow? row)
    {
        if (ReferenceEquals(_dropRow, row))
            return;
        if (_dropRow is not null)
            _dropRow.IsDropTarget = false;
        _dropRow = row;
        if (_dropRow is not null)
            _dropRow.IsDropTarget = true;
    }

    private void HighlightCard(CategoryCardModel? card)
    {
        if (ReferenceEquals(_dropCard, card))
            return;
        if (_dropCard is not null)
            _dropCard.IsDropTarget = false;
        _dropCard = card;
        if (_dropCard is not null)
            _dropCard.IsDropTarget = true;
    }

    private void ClearDropHighlight()
    {
        _fileDropRow = null;
        HighlightRow(null);
        HighlightCard(null);
    }


    private void OverdueList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is OverdueRow row)
            BringOverdue(row);
    }
    private void MoveOverdue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not OverdueRow row)
            return;
        BringOverdue(row);
    }

    private void MoveAllOverdue_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再移入任务。", InfoBarSeverity.Warning);
            return;
        }

        var snapshot = _overdue.ToList();
        if (snapshot.Count == 0)
            return;

        foreach (var row in snapshot)
            BringOverdue(row, persist: false, remember: false);
        if (!Persist())
            return;
        RememberHandled(snapshot.Select(row => row.Item));
        UpdateStats();
        RefreshOverdue();
    }

    private void BringOverdue(OverdueRow overdue, bool persist = true, bool remember = true)
    {
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再移入任务。", InfoBarSeverity.Warning);
            return;
        }

        var exists = _rows.Any(row =>
            string.Equals(row.Text.Trim(), overdue.Text, StringComparison.Ordinal)
            && DayTaskCategory.Normalize(row.Category) == DayTaskCategory.Normalize(overdue.Item.Category));
        if (!exists)
        {
            InsertRow(new TaskRow
            {
                Text = overdue.Item.Text,
                Category = overdue.Item.Category,
                Indent = overdue.Item.Indent,
                Bullet = overdue.Item.Bullet,
                DoneMark = overdue.Item.DoneMark,
                IsDone = false
            });
        }

        if (!persist)
            return;
        if (!Persist())
            return;
        if (remember)
            RememberHandled(new[] { overdue.Item });
        UpdateStats();
        RefreshOverdue();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = PinButton.IsChecked == true;
        SyncPinChrome();
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            ShowStatus("没能保存设置：" + ex.Message, InfoBarSeverity.Error);
            return;
        }

        // Only toggle pin — do not re-register hotkey or show hotkey toast.
        WindowChrome.SetAlwaysOnTop(App.Window, _settings.AlwaysOnTop);
    }

    private void SyncPinChrome()
    {
        if (PinIcon is null)
            return;
        var keyed = _settings.AlwaysOnTop ? "SoftPinCheckedForegroundBrush" : "SoftMutedInkBrush";
        if (Application.Current.Resources.TryGetValue(keyed, out var brush) && brush is Microsoft.UI.Xaml.Media.Brush b)
            PinIcon.Foreground = b;
    }

    private void Obsidian_Click(object sender, RoutedEventArgs e) => OpenInObsidian();

    private void OpenInObsidian()
    {
        if (!_settings.HasVault)
            return;

        try
        {
            var (fullPath, relative) = VaultLayout.Daily(_settings, _date);
            var vaultName = new DirectoryInfo(_settings.VaultPath).Name;
            var file = relative.Replace('\\', '/');
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                file = file[..^3];

            var uri =
                "obsidian://open?vault="
                + Uri.EscapeDataString(vaultName)
                + "&file="
                + Uri.EscapeDataString(file);
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            try
            {
                var (fullPath, _) = VaultLayout.Daily(_settings, _date);
                OpenInExplorer(File.Exists(fullPath) ? fullPath : _settings.VaultPath);
            }
            catch (Exception ex)
            {
                ShowStatus("无法打开 Obsidian：" + ex.Message, InfoBarSeverity.Error);
            }
        }
    }


    private async void Plan_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.HasVault)
        {
            ShowStatus("先连接 Obsidian 库，再生成计划。", InfoBarSeverity.Warning);
            return;
        }

        if (!_settings.IsApiConfigured)
        {
            ShowStatus("还没配置 AI 接口。打开设置填写 Base URL、API Key 和模型后，才能用自然语言生成计划。", InfoBarSeverity.Warning, "打开设置", () => OpenSettings());
            return;
        }

        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再生成计划。", InfoBarSeverity.Warning);
            return;
        }

        var inputBox = SoftUi.CreateDialogTextBox(
            "例如：今天下午写周报，晚上健身30分钟；明天把设置页改完",
            acceptsReturn: true,
            minHeight: 120,
            maxHeight: 220);
        var inputDialog = SoftUi.CreateDialog(XamlRoot, "用自然语言生成计划", inputBox, "生成预览");
        if (await inputDialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var text = (inputBox.Text ?? "").Trim();
        if (text.Length == 0)
        {
            ShowStatus("先写几句想做的事，再生成计划。", InfoBarSeverity.Warning);
            return;
        }

        SetAiBusy(true);
        ShowStatus("正在用 AI 拆解计划…", InfoBarSeverity.Informational);

        PlanGenerateResult result;
        try
        {
            result = await _plans.GenerateAsync(
                _settings,
                text,
                DateOnly.FromDateTime(DateTime.Now),
                KnownCategories(),
                _pageCts.Token);
        }
        catch (Exception ex)
        {
            result = PlanGenerateResult.Failed(ex.Message);
        }
        finally
        {
            if (!_closed)
                SetAiBusy(false);
        }

        if (_closed)
            return;

        if (result.Status == PlanGenerateStatus.NotConfigured)
        {
            ShowStatus(result.Message, InfoBarSeverity.Warning, "打开设置", () => OpenSettings());
            return;
        }

        if (result.Status != PlanGenerateStatus.Ok)
        {
            ShowStatus(result.Message, InfoBarSeverity.Error);
            return;
        }

        var lines = result.Tasks.Select(task =>
        {
            var cat = DayTaskCategory.IsUncategorized(task.Category)
                ? "未分类"
                : task.Category;
            return $"{task.Date:M/d} · {cat} · {task.Text}";
        }).ToList();

        var preview = SoftUi.CreateDialogBody(string.Join(Environment.NewLine, lines), maxWidth: 420);
        var scroll = new ScrollViewer
        {
            Content = preview,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var previewDialog = SoftUi.CreateDialog(
            XamlRoot,
            $"确认写入 {result.Tasks.Count} 条任务？",
            scroll,
            "写入笔记");
        if (await previewDialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            CommitFocusedText();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var touchesToday = result.Tasks.Any(t => t.Date == _date);

            // Persist current day UI first if we will rewrite the viewed day from disk after apply.
            if (touchesToday)
                Persist();

            var added = PlanDraftApply.Apply(_settings, result.Tasks);
            RememberCategories(result.Tasks.Select(task => task.Category));
            NoteSyncSuccess();

            if (result.Tasks.Any(t => t.Date == _date))
                LoadDay();
            else
                RefreshOverdue();

            var otherDays = result.Tasks.Select(t => t.Date).Distinct().Count(d => d != _date);
            var msg = otherDays == 0
                ? $"已写入 {added} 条任务到今天。"
                : $"已写入 {added} 条任务（含 {otherDays} 个其它日期的笔记）。";
            ShowStatus(msg, InfoBarSeverity.Success);
            _ = today;
        }
        catch (Exception ex)
        {
            NoteSyncFailure();
            ShowStatus("写入失败：" + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.HasVault)
        {
            ShowStatus("先连接 Obsidian 库，再生成本月总结。", InfoBarSeverity.Warning);
            return;
        }
        if (!_settings.IsApiConfigured)
        {
            ShowStatus("还没配置 AI 接口。打开设置填写 Base URL、API Key 和模型后，才能生成本月总结。", InfoBarSeverity.Warning, "打开设置", () => OpenSettings());
            return;
        }

        CommitFocusedText();
        Persist();

        string relative;
        string fullPath;
        try
        {
            (fullPath, relative) = VaultLayout.Report(_settings, _date);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        var monthLabel = _date.ToString("yyyy年M月", DisplayCulture);
        var exists = File.Exists(fullPath);
        var body = exists
            ? $"将扫描 {monthLabel} 各日笔记里的待办小节（不含灵感），用 AI 生成本月总结，并覆盖已有文件：\n{relative}"
            : $"将扫描 {monthLabel} 各日笔记里的待办小节（不含灵感），用 AI 生成本月总结，并写入：\n{relative}";

        var confirm = SoftUi.CreateDialog(
            XamlRoot,
            exists ? "生成并覆盖本月总结？" : "生成本月总结？",
            SoftUi.CreateDialogBody(body),
            exists ? "覆盖生成" : "生成");
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        SetAiBusy(true);
        ShowStatus("正在用 AI 生成本月总结…", InfoBarSeverity.Informational);
        try
        {
            var result = await _reports.GenerateAsync(_settings, _date, _pageCts.Token);
            if (_closed)
                return;
            switch (result.Status)
            {
                case MonthlyReportStatus.Saved:
                    NoteSyncSuccess();
                    ShowStatus(result.Message, InfoBarSeverity.Success, "定位到", () => OpenInExplorer(result.FullPath!));
                    break;
                case MonthlyReportStatus.EmptyMonth:
                    ShowStatus(result.Message, InfoBarSeverity.Informational);
                    break;
                default:
                    ShowStatus(result.Message, InfoBarSeverity.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!_closed)
                ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (!_closed)
                SetAiBusy(false);
        }
    }

    private void SetAiBusy(bool busy)
    {
        if (ReportSidebarButton is not null)
            ReportSidebarButton.IsEnabled = !busy;
        if (PlanSidebarButton is not null)
            PlanSidebarButton.IsEnabled = !busy;
        if (ReportRing is not null)
        {
            ReportRing.IsActive = busy;
            ReportRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ReportSidebarIcon is not null)
            ReportSidebarIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Explorer_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.HasVault)
            OpenInExplorer(_settings.VaultPath);
    }

    private async void PickVault_Click(object sender, RoutedEventArgs e)
    {
        var pick = await FolderBrowser.PickAsync(App.Window);
        if (pick.Error is not null)
        {
            ShowStatus(pick.Error, InfoBarSeverity.Error);
            OpenSettings();
            return;
        }

        if (pick.Path is not null)
            OpenSettings(pick.Path);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings(string? pickedVault = null)
    {
        if (_settingsWindow is not null)
        {
            if (pickedVault is not null)
                _settingsWindow.ApplyVault(pickedVault);
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settings.Copy());
        _settingsWindow = window;
        if (pickedVault is not null)
            window.ApplyVault(pickedVault);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (_closed || window.Result is not AppSettings saved)
                return;
            // Preserve live window placement fields if settings window didn't edit them.
            saved.WindowLeft = _settings.WindowLeft;
            saved.WindowTop = _settings.WindowTop;
            saved.WindowWidth = _settings.WindowWidth;
            saved.WindowHeight = _settings.WindowHeight;
            saved.FocusMinutes = _focus.Duration.FocusMinutes;
            saved.BreakMinutes = _focus.Duration.BreakMinutes;
            saved.TaskCategories = _settings.TaskCategories?.ToList() ?? new List<string>();
            saved.RetiredTaskCategories = _settings.RetiredTaskCategories?.ToList() ?? new List<string>();
            _settings = saved;
            try
            {
                SettingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                ShowStatus("没能保存设置：" + ex.Message, InfoBarSeverity.Error);
                return;
            }

            PinButton.IsChecked = _settings.AlwaysOnTop;
            SyncPinChrome();
            _host?.ReloadShellSettings(_settings);
            WindowChrome.SetAlwaysOnTop(App.Window, _settings.AlwaysOnTop);
            ShowMode();
            if (_settings.HasVault)
                LoadDay();
        };
        window.Activate();
    }

    private void ExternalBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (Suppressed || !_pendingExternal)
            return;
        _pendingExternal = false;
        Persist();
    }

    private void LoadExternal_Click(object sender, RoutedEventArgs e)
    {
        _pendingExternal = false;
        PushSuppress();
        ExternalBar.IsOpen = false;
        PopSuppress();
        ReloadFromDisk();
    }

    private bool Persist()
    {
        if (Suppressed || _pendingExternal || !_settings.HasVault || _closed)
            return false;
        try
        {
            var result = DailyNoteStore.Write(_settings, _date, CurrentTasks());
            _knownHash = result.ContentHash;
            ArmWatcher(result.FullPath);
            PathText.Text = FormatNoteSubtitle(result.RelativePath, File.Exists(result.FullPath));
            RefreshSectionTitleChrome();
            _noteMatchesDisk = true;
            NoteVaultWrite(result);
            return true;
        }
        catch (Exception ex)
        {
            _noteMatchesDisk = false;
            NoteSyncFailure();
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return false;
        }
    }

    private void NoteVaultWrite(WriteResult result)
    {
        _syncError = false;
        if (result.Wrote)
            _lastSyncAt = DateTimeOffset.Now;
        RenderSyncCapsule();
        if (result.Wrote)
            EnsureSyncClock();
    }

    private void NoteSyncSuccess()
    {
        _syncError = false;
        _lastSyncAt = DateTimeOffset.Now;
        RenderSyncCapsule();
        EnsureSyncClock();
    }

    private void NoteSyncFailure()
    {
        _syncError = true;
        RenderSyncCapsule();
    }

    private void RenderSyncCapsule()
    {
        if (SyncCapsule is null || SyncLabel is null || SyncDot is null)
            return;
        if (!_settings.HasVault)
        {
            SyncCapsule.Visibility = Visibility.Collapsed;
            return;
        }

        SyncCapsule.Visibility = Visibility.Visible;
        var phase = _syncError
            ? SyncPhase.Error
            : _lastSyncAt is null ? SyncPhase.Idle : SyncPhase.Saved;
        SyncLabel.Text = SyncStatusText.Format(phase, _lastSyncAt, DateTimeOffset.Now);
        SyncDot.Fill = new SolidColorBrush(phase switch
        {
            SyncPhase.Error => Color.FromArgb(255, 0xC4, 0x6A, 0x4A),
            SyncPhase.Saved => Color.FromArgb(255, 0x6B, 0xB5, 0x6C),
            _ => Color.FromArgb(255, 0xC4, 0xB0, 0x9A)
        });
    }

    private void EnsureSyncClock()
    {
        if (_syncClock is not null)
            return;
        _syncClock = DispatcherQueue.CreateTimer();
        _syncClock.Interval = TimeSpan.FromSeconds(15);
        _syncClock.IsRepeating = true;
        _syncClock.Tick += (_, _) =>
        {
            if (!_closed)
                RenderSyncCapsule();
        };
        _syncClock.Start();
    }

    private void EnsureTemplateButtons()
    {
        if (_templatesReady || TemplateHost is null)
            return;
        _templatesReady = true;
        foreach (var template in DayTemplates.Starters)
        {
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = template.Title,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["SoftInkBrush"]
            });
            stack.Children.Add(new TextBlock
            {
                Text = template.Blurb,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SoftMutedInkBrush"],
                TextWrapping = TextWrapping.Wrap
            });
            var button = new Button
            {
                Content = stack,
                Tag = template.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = (Brush)Application.Current.Resources["SoftCardBrush"],
                BorderBrush = (Brush)Application.Current.Resources["SoftLineBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 10, 14, 10)
            };
            button.Click += Template_Click;
            TemplateHost.Children.Add(button);
        }
    }

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string id)
            return;
        ApplyTemplate(id);
    }

    private void ApplyTemplate(string id)
    {
        var template = DayTemplates.Find(id);
        if (template is null || !_settings.HasVault || Suppressed)
            return;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再添加任务。", InfoBarSeverity.Warning);
            return;
        }

        var tasks = template.CreateTasks();
        RememberCategories(tasks.Select(task => task.Category));
        _suspendRowRebuild = true;
        try
        {
            foreach (var task in tasks)
                InsertRow(TaskRow.From(task, _settings.VaultPath));
        }
        finally
        {
            _suspendRowRebuild = false;
        }

        RebuildList();
        UpdateStats();
        RefreshCategoryPicker();
        Persist();
        ShowStatus($"已放入「{template.Title}」。", InfoBarSeverity.Success);
    }

    private static List<TaskDoneSnapshot> DoneSnapshots(IEnumerable<TaskRow> rows) =>
        rows.Select(row => new TaskDoneSnapshot(row.Category, row.IsDone)).ToList();

    private List<TaskDoneSnapshot> DoneSnapshots() => DoneSnapshots(_rows);

    private void PlayMoment(CompletionMoment moment)
    {
        if (_closed || !moment.Any || CelebrationBanner is null || CelebrationText is null)
            return;

        string text;
        var pulseHost = false;
        if (moment.DayJustCompleted)
        {
            text = "今天都完成了";
            pulseHost = true;
        }
        else if (moment.CategoryJustCompleted is string category)
        {
            text = DayTaskCategory.Display(category) + "，这一类完成了";
        }
        else
        {
            return;
        }

        // Confetti only for the day. A finished category keeps the banner alone.
        var confetti = ConfettiChoreography.ForDay(moment);

        // Checked is still on the stack. The banner was Collapsed, and the
        // day path also used to storyboard a ScaleTransform on SideProgressRing.
        // WinUI originates 0x800F1000 (Cannot resolve TargetName) for that
        // Begin(); the XAML dispatcher then fail-fasts with 0xC000027B before
        // the next frame, so the toast never appears. Wait until layout has
        // given the banner a slot, and pulse the host grid — never the ring.
        // Confetti is a composition child of ConfettiHost, which is never
        // collapsed and is not a storyboard target.
        var token = ++_celebrationToken;
        CelebrationText.Text = text;
        CelebrationBanner.Opacity = 0;
        CelebrationBanner.Visibility = Visibility.Visible;
        StopMomentStoryboards();
        _confetti.Clear();

        var queued = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => StartCelebration(token, pulseHost, confetti));
        if (!queued)
            ShowCelebrationTextOnly(token, confetti);
    }

    private void StartCelebration(int token, bool pulseHost, bool confetti)
    {
        if (_closed || token != _celebrationToken || CelebrationBanner is null)
            return;

        try
        {
            CelebrationBanner.UpdateLayout();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        if (!CelebrationBanner.IsLoaded || !TryRestart(StoryboardResource("CelebrationStoryboard")))
        {
            ShowCelebrationTextOnly(token, confetti);
            return;
        }

        if (pulseHost && SideProgressHost is { IsLoaded: true })
            TryRestart(StoryboardResource("ProgressPulseStoryboard"));

        if (confetti)
            PlayConfetti();

        ArmCelebrationHide(token);
    }

    private void ShowCelebrationTextOnly(int token, bool confetti)
    {
        if (_closed || token != _celebrationToken || CelebrationBanner is null)
            return;

        StopMomentStoryboards();
        CelebrationBanner.Opacity = 1;
        CelebrationBanner.Visibility = Visibility.Visible;
        if (confetti)
            PlayConfetti();
        ArmCelebrationHide(token);
    }

    private void PlayConfetti()
    {
        if (ConfettiHost is null)
            return;
        _confetti.Play(ConfettiHost, ConfettiChoreography.Pieces(Environment.TickCount));
    }

    private void ArmCelebrationHide(int token)
    {
        _celebrationHideToken = token;
        if (_celebrationTimer is null)
        {
            _celebrationTimer = DispatcherQueue.CreateTimer();
            _celebrationTimer.IsRepeating = false;
            _celebrationTimer.Tick += (_, _) =>
            {
                _celebrationTimer?.Stop();
                if (_closed || _celebrationHideToken != _celebrationToken || CelebrationBanner is null)
                    return;
                StopMomentStoryboards();
                _confetti.Clear();
                CelebrationBanner.Opacity = 0;
                CelebrationBanner.Visibility = Visibility.Collapsed;
            };
        }

        _celebrationTimer.Stop();
        _celebrationTimer.Interval = TimeSpan.FromMilliseconds(1700);
        _celebrationTimer.Start();
    }

    private Storyboard? StoryboardResource(string key) =>
        Resources.TryGetValue(key, out var value) ? value as Storyboard : null;

    private static bool TryRestart(Storyboard? storyboard)
    {
        if (storyboard is null)
            return false;

        // Stop on a storyboard that has never been started can throw.
        // That must not skip Begin — the first celebration would otherwise
        // always fall through to the static toast.
        try
        {
            storyboard.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        try
        {
            storyboard.Begin();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            try
            {
                storyboard.Stop();
            }
            catch (Exception stopEx)
            {
                Debug.WriteLine(stopEx);
            }

            return false;
        }
    }

    private void StopMomentStoryboards()
    {
        TryStop(StoryboardResource("CelebrationStoryboard"));
        TryStop(StoryboardResource("ProgressPulseStoryboard"));
    }

    private static void TryStop(Storyboard? storyboard)
    {
        if (storyboard is null)
            return;
        try
        {
            storyboard.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void PlayCheckPop(CheckBox box) => PlayScale(box, 12, 1.16);

    private void PlayScale(FrameworkElement element, double center, double peak = 1.08)
    {
        // A determinate ProgressRing owns its composition visual. Assigning
        // RenderTransform and storyboarding it is the day-complete crash.
        if (_closed || element is ProgressRing || !element.IsLoaded || element.Visibility != Visibility.Visible)
            return;

        var scale = element.RenderTransform as ScaleTransform;
        if (scale is null)
        {
            scale = new ScaleTransform { ScaleX = 1, ScaleY = 1, CenterX = center, CenterY = center };
            element.RenderTransform = scale;
        }
        else
        {
            scale.CenterX = center;
            scale.CenterY = center;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        var storyboard = new Storyboard();
        var scaleX = ScaleFrames(peak);
        var scaleY = ScaleFrames(peak);
        Storyboard.SetTarget(scaleX, scale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, scale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        try
        {
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private static DoubleAnimationUsingKeyFrames ScaleFrames(double peak)
    {
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)),
            Value = peak
        });
        frames.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
            Value = 1
        });
        return frames;
    }

    private void HideCelebration()
    {
        _celebrationToken++;
        _celebrationTimer?.Stop();
        StopMomentStoryboards();
        _confetti.Clear();
        if (CelebrationBanner is null)
            return;
        CelebrationBanner.Opacity = 0;
        CelebrationBanner.Visibility = Visibility.Collapsed;
    }

    private void ReloadFromDisk()
    {
        if (_closed || !_settings.HasVault || Suppressed)
            return;

        DailyNoteSnapshot snapshot;
        try
        {
            snapshot = DailyNoteStore.Read(_settings, _date);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (snapshot.ContentHash == _knownHash)
        {
            ClearExternal();
            return;
        }

        var tasksSame = DayTask.SameList(snapshot.Tasks, CurrentTasks());
        var inspirationsSame = InspirationEntry.SameList(snapshot.Inspirations, _inspirations);
        if (tasksSame && !inspirationsSame && _inspirationDialogOpen)
            return;

        if (!inspirationsSame && !_inspirationDialogOpen)
            ApplyInspirations(snapshot.Inspirations);

        if (tasksSame)
        {
            _knownHash = snapshot.ContentHash;
            ClearExternal();
            return;
        }

        if (_textFocus > 0)
        {
            _pendingExternal = true;
            ExternalBar.IsOpen = true;
            return;
        }

        ClearExternal();
        PushSuppress();
        try
        {
            Apply(snapshot);
            RefreshOverdue();
        }
        finally
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PopSuppress);
        }
    }

    private void ArmWatcher(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            _watcher?.Dispose();
            _watcher = null;
            _watchedPath = null;
            return;
        }

        if (_watcher is not null && string.Equals(_watchedPath, fullPath, StringComparison.OrdinalIgnoreCase))
            return;

        _watcher?.Dispose();
        try
        {
            var watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, _) => QueueReload();
            watcher.Created += (_, _) => QueueReload();
            watcher.Deleted += (_, _) => QueueReload();
            watcher.Renamed += (_, _) => QueueReload();
            _watcher = watcher;
            _watchedPath = fullPath;
        }
        catch (Exception ex)
        {
            _watcher = null;
            _watchedPath = null;
            ShowStatus("无法监视笔记文件：" + ex.Message, InfoBarSeverity.Warning);
        }
    }

    private void QueueReload()
    {
        if (_closed)
            return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
                return;
            _watchTimer?.Stop();
            _watchTimer?.Start();
        });
    }

    private void CommitFocusedText()
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox box && box.DataContext is TaskRow row)
            row.Text = box.Text ?? "";
    }

    private List<DayTask> CurrentTasks() => _rows.Select(row => row.ToTask()).ToList();

    private string FormatNoteSubtitle(string relativePath, bool fileExists)
    {
        if (!fileExists)
            return $"{relativePath}（尚未创建）";
        return relativePath;
    }

    private void SetSectionChromeVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        SectionPrefix.Visibility = v;
        SectionTitleChrome.Visibility = v;
        if (!_editingSection)
        {
            SectionTitleText.Visibility = Visibility.Visible;
            SectionTitleBox.Visibility = Visibility.Collapsed;
            SectionTitleChrome.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private void RefreshSectionTitleChrome()
    {
        if (!_settings.HasVault)
        {
            SetSectionChromeVisible(false);
            return;
        }

        var (_, title) = HeadingSpec.Parse(_settings.SectionHeading);
        if (string.IsNullOrWhiteSpace(title))
            title = "DayList";
        SectionTitleText.Text = title;
        SetSectionChromeVisible(true);
    }


    private void SectionTitleChrome_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_editingSection || SectionTitleChrome is null)
            return;
        SectionTitleChrome.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftNavSelectedBrush"];
        SectionTitleText.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
    }

    private void SectionTitleChrome_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_editingSection || SectionTitleChrome is null)
            return;
        SectionTitleChrome.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SectionTitleText.TextDecorations = Windows.UI.Text.TextDecorations.None;
    }

    private void SectionTitle_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (!_settings.HasVault || _editingSection)
            return;
        BeginSectionEdit();
    }

    private void BeginSectionEdit()
    {
        var (_, title) = HeadingSpec.Parse(_settings.SectionHeading);
        if (string.IsNullOrWhiteSpace(title))
            title = "DayList";
        _editingSection = true;
        SectionTitleChrome.Visibility = Visibility.Visible;
        SectionTitleChrome.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftNavSelectedBrush"];
        SectionTitleText.Visibility = Visibility.Collapsed;
        SectionTitleBox.Visibility = Visibility.Visible;
        SectionTitleBox.Text = title;
        SectionTitleBox.SelectAll();
        SectionTitleBox.Focus(FocusState.Programmatic);
    }

    private void CancelSectionEdit()
    {
        if (!_editingSection)
            return;
        _editingSection = false;
        _sectionCommitPending = false;
        SectionTitleBox.Visibility = Visibility.Collapsed;
        SectionTitleText.Visibility = Visibility.Visible;
        SectionTitleChrome.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RefreshSectionTitleChrome();
    }

    private void SectionTitleBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelSectionEdit();
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _sectionCommitPending = true;
            CommitSectionTitle();
        }
    }

    private void SectionTitleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_editingSection)
            return;
        if (_sectionCommitPending)
        {
            _sectionCommitPending = false;
            return;
        }
        CommitSectionTitle();
    }

    private void CommitSectionTitle()
    {
        if (!_editingSection)
            return;

        var raw = (SectionTitleBox.Text ?? "").Trim();
        if (raw.Length == 0)
            raw = "DayList";

        var (level, oldTitle) = HeadingSpec.Parse(_settings.SectionHeading);
        if (string.IsNullOrWhiteSpace(oldTitle))
            oldTitle = "DayList";

        _editingSection = false;
        SectionTitleBox.Visibility = Visibility.Collapsed;
        SectionTitleText.Visibility = Visibility.Visible;
        SectionTitleChrome.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (string.Equals(raw, oldTitle, StringComparison.Ordinal))
        {
            RefreshSectionTitleChrome();
            return;
        }

        var oldHeading = _settings.SectionHeading;
        _settings.SectionHeading = HeadingSpec.Format(level, raw);
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            _settings.SectionHeading = oldHeading;
            RefreshSectionTitleChrome();
            ShowStatus("没能保存小节标题：" + ex.Message, InfoBarSeverity.Error);
            return;
        }

        try
        {
            if (_settings.HasVault)
            {
                var rewritten = DailyNoteStore.RewriteSectionHeading(_settings, _date, oldHeading);
                if (rewritten.Wrote)
                    _knownHash = rewritten.ContentHash;
                NoteVaultWrite(rewritten);
            }
        }
        catch (Exception ex)
        {
            NoteSyncFailure();
            ShowStatus("小节标题已保存，但笔记标题没改成功：" + ex.Message, InfoBarSeverity.Warning);
        }

        RefreshSectionTitleChrome();
        if (_settings.HasVault)
            LoadDay();
        else
            ShowStatus($"小节标题已改为「{raw}」", InfoBarSeverity.Success);
    }

    private sealed class RowHoverState
    {
        public int Generation;
        public bool PointerInside;
    }

    private static readonly ConditionalWeakTable<Grid, RowHoverState> RowHoverStates = new();

    private static RowHoverState HoverState(Grid grid) => RowHoverStates.GetOrCreateValue(grid);

    private void TaskRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (FindTaskRowGrid(sender) is not Grid grid)
            return;
        var state = HoverState(grid);
        state.Generation++;
        state.PointerInside = true;
        if (_pointerRow is Grid previous && !ReferenceEquals(previous, grid))
        {
            var previousState = HoverState(previous);
            previousState.PointerInside = false;
            previousState.Generation++;
            if (previous.IsLoaded && !RowContainsFocus(previous))
                SetRowHoverActions(previous, shown: false);
        }

        _pointerRow = grid;
        SetRowHoverActions(grid, shown: true);
    }

    private void TaskRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (FindTaskRowGrid(sender) is not Grid grid)
            return;
        // Moving onto a child (the buttons) also raises PointerExited on the row.
        var point = e.GetCurrentPoint(grid).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X <= grid.ActualWidth && point.Y <= grid.ActualHeight)
            return;

        var state = HoverState(grid);
        state.PointerInside = false;
        var generation = ++state.Generation;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!grid.IsLoaded)
                return;
            var current = HoverState(grid);
            if (current.Generation != generation || current.PointerInside || RowContainsFocus(grid))
                return;
            if (ReferenceEquals(_pointerRow, grid))
                _pointerRow = null;
            SetRowHoverActions(grid, shown: false);
        });
    }

    private void TaskRow_GotFocus(object sender, RoutedEventArgs e)
    {
        if (FindTaskRowGrid(sender) is Grid grid)
            SetRowHoverActions(grid, shown: true);
    }

    private void TaskRow_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FindTaskRowGrid(sender) is not Grid grid)
            return;
        // Focus may already be on the next control inside this row. Check after the move.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!grid.IsLoaded || HoverState(grid).PointerInside || RowContainsFocus(grid))
                return;
            SetRowHoverActions(grid, shown: false);
        });
    }

    private static Grid? FindTaskRowGrid(object sender)
    {
        if (sender is not DependencyObject current)
            return null;
        for (; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Grid grid && Equals(grid.Tag, "task-row"))
                return grid;
        }

        return null;
    }

    private static bool RowContainsFocus(Grid grid)
    {
        if (grid.XamlRoot is null)
            return false;
        if (FocusManager.GetFocusedElement(grid.XamlRoot) is not DependencyObject focused)
            return false;
        for (DependencyObject? current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, grid))
                return true;
        }

        return false;
    }

    private static void SetRowHoverActions(Grid grid, bool shown)
    {
        var actions = FindTagged(grid, "rowHoverActions");
        var status = FindTagged(grid, "rowStatus");
        if (actions is not null)
            actions.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (status is not null)
            status.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
    }

    private static FrameworkElement? FindTagged(DependencyObject root, string tag)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && Equals(element.Tag, tag))
                return element;
            var nested = FindTagged(child, tag);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void ShowStatus(string message, InfoBarSeverity severity, string? action = null, Action? onAction = null)
    {
        if (_closed)
            return;
        StatusBar.Severity = severity;
        StatusBar.Title = "";
        StatusBar.Message = message;
        StatusBar.Background = severity switch
        {
            InfoBarSeverity.Success => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftInfoBarSuccessBrush"],
            InfoBarSeverity.Warning => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftInfoBarWarningBrush"],
            InfoBarSeverity.Error => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftInfoBarErrorBrush"],
            _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SoftInfoBarInformationalBrush"]
        };
        if (action is null || onAction is null)
        {
            StatusBar.ActionButton = null;
        }
        else
        {
            var button = new Button { Content = action };
            button.Click += (_, _) => onAction();
            StatusBar.ActionButton = button;
        }

        StatusBar.IsOpen = true;
        _statusToken++;
        _statusTimer?.Stop();
        // Auto-dismiss brief success/info toasts; keep errors/warnings and action prompts.
        var autoDismiss = action is null
            && (severity == InfoBarSeverity.Success || severity == InfoBarSeverity.Informational);
        if (!autoDismiss)
            return;

        _statusDismissToken = _statusToken;
        if (_statusTimer is null)
        {
            _statusTimer = DispatcherQueue.CreateTimer();
            _statusTimer.IsRepeating = false;
            _statusTimer.Tick += (_, _) =>
            {
                _statusTimer?.Stop();
                if (!_closed && _statusToken == _statusDismissToken)
                    StatusBar.IsOpen = false;
            };
        }

        _statusTimer.Interval = TimeSpan.FromSeconds(3);
        _statusTimer.Start();
    }

    private void ClearExternal()
    {
        _pendingExternal = false;
        if (!ExternalBar.IsOpen)
            return;
        PushSuppress();
        ExternalBar.IsOpen = false;
        PopSuppress();
    }

    private static void OpenInExplorer(string path)
    {
        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private async void Inspiration_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.HasVault)
        {
            ShowStatus("先连接 Obsidian 库，灵感才会写进当天笔记。", InfoBarSeverity.Warning);
            return;
        }

        if (InspirationSection.SameHeading(_settings.SectionHeading, _settings.InspirationHeading))
        {
            ShowStatus("灵感小节和待办小节重名了，请在设置里改一个。", InfoBarSeverity.Warning, "打开设置", () => OpenSettings());
            return;
        }

        var drafts = _inspirations
            .Select(entry => new InspirationDraft(entry.TimeLabel, entry.Text, entry.RawLine))
            .ToList();
        var list = new StackPanel { Spacing = 8 };
        var fresh = SoftUi.CreateDialogTextBox("此刻想到的…", acceptsReturn: true, minHeight: 88, maxHeight: 160);

        void RebuildDrafts()
        {
            list.Children.Clear();
            if (drafts.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "还没有灵感。",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0x7A, 0x6F, 0x62))
                });
                return;
            }

            foreach (var draft in drafts)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var time = new TextBlock
                {
                    Text = string.IsNullOrEmpty(draft.TimeLabel) ? "·" : draft.TimeLabel,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0x7A, 0x6F, 0x62))
                };
                var box = SoftUi.CreateDialogTextBox(acceptsReturn: true, minHeight: 36, maxHeight: 96);
                box.Text = draft.Text;
                box.TextChanged += (_, _) => draft.Text = box.Text ?? "";
                Grid.SetColumn(box, 1);
                var delete = new Button
                {
                    Content = "删除",
                    Padding = new Thickness(10, 6, 10, 6),
                    VerticalAlignment = VerticalAlignment.Center
                };
                delete.Click += (_, _) =>
                {
                    drafts.Remove(draft);
                    RebuildDrafts();
                };
                Grid.SetColumn(delete, 2);
                row.Children.Add(time);
                row.Children.Add(box);
                row.Children.Add(delete);
                list.Children.Add(row);
            }
        }

        RebuildDrafts();

        var (_, inspirationTitle) = HeadingSpec.Parse(_settings.InspirationHeading);
        var panel = new StackPanel { Spacing = 10, MaxWidth = 440 };
        panel.Children.Add(SoftUi.CreateDialogBody(
            $"写一句，点保存。记在「{inspirationTitle}」。"));
        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        panel.Children.Add(fresh);

        var dialog = SoftUi.CreateDialog(
            XamlRoot,
            "灵感 · " + _date.ToString("M月d日", DisplayCulture),
            panel,
            "保存");

        _inspirationDialogOpen = true;
        var attemptedWrite = false;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var kept = new List<InspirationEntry>();
            foreach (var draft in drafts)
            {
                var text = (draft.Text ?? "").Trim();
                if (text.Length == 0)
                    continue;
                kept.Add(new InspirationEntry
                {
                    TimeLabel = draft.TimeLabel,
                    Text = text,
                    RawLine = draft.RawLine
                });
            }

            var added = (fresh.Text ?? "").Trim();
            if (added.Length > 0)
            {
                kept.Add(new InspirationEntry
                {
                    TimeLabel = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Text = added
                });
            }

            attemptedWrite = true;
            var result = DailyNoteStore.WriteInspirations(_settings, _date, kept);
            NoteVaultWrite(result);
            ApplyInspirations(kept);
            ArmWatcher(result.FullPath);
            if (File.Exists(result.FullPath))
                PathText.Text = FormatNoteSubtitle(result.RelativePath, true);
            ShowStatus(kept.Count == 0 ? "已清空这一天的灵感。" : "已写入灵感。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            if (attemptedWrite)
                NoteSyncFailure();
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _inspirationDialogOpen = false;
            if (!_closed)
                ReloadFromDisk();
        }
    }

    private async void Remind_Click(object sender, RoutedEventArgs e)
    {
        if (Suppressed || (sender as FrameworkElement)?.DataContext is not TaskRow row)
            return;
        await PromptReminderAsync(row);
    }

    private async Task PromptReminderAsync(TaskRow row)
    {
        if (Suppressed)
            return;
        if (_pendingExternal)
        {
            ShowStatus("先处理笔记的外部更新，再设置提醒。", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Text))
        {
            ShowStatus("先写上任务内容，再设提醒。", InfoBarSeverity.Warning);
            return;
        }

        var wheels = new ReminderTimeWheels(ReminderSchedule.SuggestTime(_date, DateTime.Now, row.ReminderTime));
        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xB3, 0x3A, 0x3A))
        };
        var panel = new StackPanel { Spacing = 8, MaxWidth = 380 };
        panel.Children.Add(SoftUi.CreateDialogBody("上下滚动选择时间，再点保存。"));
        panel.Children.Add(wheels);
        panel.Children.Add(error);

        var dialog = SoftUi.CreateDialog(
            XamlRoot,
            "提醒",
            panel,
            "保存",
            "取消",
            ContentDialogButton.Primary,
            string.IsNullOrEmpty(row.ReminderId) ? null : "清除");
        dialog.Opened += (_, _) => wheels.FocusHour();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (_date.ToDateTime(wheels.Time) <= DateTime.Now)
            {
                args.Cancel = true;
                error.Text = "这个时间已经过了。";
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;

        try
        {
            if (result == ContentDialogResult.Secondary)
            {
                ForgetReminder(row);
                ShowStatus("已清除提醒。", InfoBarSeverity.Success);
                return;
            }

            var time = wheels.Time;
            var saved = ReminderStore.SetForTask(
                ReminderStore.DefaultPath,
                row.ReminderId,
                row.Text,
                _date,
                time);
            row.ReminderId = saved.Id;
            row.ReminderTime = saved.Time;
            ShowStatus($"会在 {_date.ToString("M月d日", DisplayCulture)} {saved.Time} 提醒。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("没能保存提醒：" + ex.Message, InfoBarSeverity.Error);
        }
    }

    public void HandleReminderAction(string action, string id)
    {
        if (_closed || string.IsNullOrWhiteSpace(id))
            return;

        if (string.Equals(action, "snooze", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ReminderStore.Snooze(ReminderStore.DefaultPath, id, DateTime.Now, 10);
                _reminderInflight.Remove(id);
                BindReminders();
                ShowStatus("已延后 10 分钟。", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("没能延后提醒：" + ex.Message, InfoBarSeverity.Warning);
            }

            return;
        }

        TaskReminder? reminder = null;
        try
        {
            reminder = ReminderStore.Find(ReminderStore.DefaultPath, id);
        }
        catch (Exception ex)
        {
            ShowStatus("没能读取提醒：" + ex.Message, InfoBarSeverity.Warning);
            return;
        }

        _host?.BringToFront();
        if (reminder is null)
        {
            ShowStatus("这条提醒已经不在了。", InfoBarSeverity.Informational);
            return;
        }

        FocusReminderTask(reminder.Date, reminder.TaskText);
    }

    private void CheckReminders()
    {
        if (_closed)
            return;

        List<TaskReminder> all;
        try
        {
            all = ReminderStore.Load(ReminderStore.DefaultPath).ToList();
        }
        catch
        {
            return;
        }

        var due = ReminderSchedule.Partition(all, DateTime.Now);
        var notify = new List<TaskReminder>();
        foreach (var item in due.Notify)
        {
            if (_reminderInflight.Add(item.Id))
                notify.Add(item);
        }

        if (due.Expire.Count == 0 && notify.Count == 0)
            return;

        foreach (var item in due.Expire)
            item.Fired = true;
        foreach (var item in notify)
            item.Fired = true;

        try
        {
            ReminderStore.Save(ReminderStore.DefaultPath, all);
        }
        catch (Exception ex)
        {
            foreach (var item in notify)
                _reminderInflight.Remove(item.Id);
            ShowStatus("没能保存提醒：" + ex.Message, InfoBarSeverity.Warning);
            return;
        }

        foreach (var item in notify)
            EnqueueReminder(CopyReminder(item));
        if (!_closed)
            BindReminders();
    }

    private void EnqueueReminder(TaskReminder reminder)
    {
        _dueReminders.Enqueue(reminder);
        _ = DrainRemindersAsync();
    }

    private async Task DrainRemindersAsync()
    {
        if (_showingReminder || _closed)
            return;

        _showingReminder = true;
        try
        {
            while (_dueReminders.Count > 0 && !_closed)
            {
                var reminder = _dueReminders.Dequeue();
                await PresentReminderAsync(reminder);
            }
        }
        finally
        {
            _showingReminder = false;
            if (_dueReminders.Count > 0 && !_closed)
                _ = DrainRemindersAsync();
        }
    }

    private Task PresentReminderAsync(TaskReminder reminder)
    {
        // Corner card only. Never activate the main window from a due reminder.
        if (_settings.ReminderToastOnly && ReminderNotifier.TryShow(reminder, _settings.ReminderSound))
        {
            _reminderInflight.Remove(reminder.Id);
            return Task.CompletedTask;
        }

        try
        {
            ReminderPopup.Show(
                reminder,
                playSound: _settings.ReminderSound,
                onOpen: () =>
                {
                    _host?.BringToFront();
                    FocusReminderTask(reminder.Date, reminder.TaskText);
                },
                onSnooze: () =>
                {
                    try
                    {
                        ReminderStore.Snooze(ReminderStore.DefaultPath, reminder.Id, DateTime.Now, 10);
                        BindReminders();
                    }
                    catch (Exception ex)
                    {
                        ShowStatus("没能延后提醒：" + ex.Message, InfoBarSeverity.Warning);
                    }
                },
                onRetimed: () => BindReminders(),
                onDismiss: () => _reminderInflight.Remove(reminder.Id));
        }
        catch
        {
            _reminderInflight.Remove(reminder.Id);
            if (!ReminderNotifier.TryShow(reminder, _settings.ReminderSound))
                ShowStatus("提醒没能弹出：" + reminder.TaskText, InfoBarSeverity.Warning);
        }

        return Task.CompletedTask;
    }

    private void FocusReminderTask(DateOnly date, string taskText)
    {
        if (!_settings.HasVault)
        {
            ShowStatus("提醒：" + taskText, InfoBarSeverity.Informational);
            return;
        }

        if (_date != date)
            SetDate(date);
        if (_nav != MainNav.Today)
        {
            _nav = MainNav.Today;
            RebuildList();
            SyncNavChrome();
        }

        ShowStatus("提醒：" + taskText, InfoBarSeverity.Informational);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusRowByText(taskText));
    }

    private void FocusRowByText(string taskText)
    {
        var row = _rows.FirstOrDefault(item =>
            string.Equals((item.Text ?? "").Trim(), taskText.Trim(), StringComparison.Ordinal));
        if (row is null)
            return;

        FindTaskBox(WorkBody, row)?.Focus(FocusState.Programmatic);
    }

    private static TextBox? FindTaskBox(DependencyObject root, TaskRow row)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box && ReferenceEquals(box.DataContext, row))
                return box;
            var nested = FindTaskBox(child, row);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void ApplyInspirations(IReadOnlyList<InspirationEntry> entries)
    {
        _inspirations.Clear();
        foreach (var entry in entries)
        {
            _inspirations.Add(new InspirationEntry
            {
                TimeLabel = entry.TimeLabel,
                Text = entry.Text,
                RawLine = entry.RawLine
            });
        }

        NavInspirationCount.Text = _inspirations.Count == 0
            ? ""
            : _inspirations.Count.ToString(CultureInfo.InvariantCulture);
        RefreshInspirationGlance();
    }

    private void RefreshInspirationGlance()
    {
        if (InspirationGlanceList is not null)
            InspirationGlanceList.ItemsSource = null;

        _inspirationGlance.Clear();
        var sources = new List<InspirationFlowSource>(_inspirations.Count);
        for (var i = 0; i < _inspirations.Count; i++)
        {
            var entry = _inspirations[i];
            sources.Add(new InspirationFlowSource(_date, i, entry.TimeLabel, entry.Text));
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var day in InspirationFlow.Arrange(_date, today, sources, 1))
        {
            foreach (var item in day.Items)
                _inspirationGlance.Add(new InspirationGlance(item.Clock, item.Glance));
        }

        if (InspirationGlanceList is not null)
            InspirationGlanceList.ItemsSource = _inspirationGlance;

        var count = _inspirationGlance.Count;
        if (InspirationStripCount is not null)
        {
            InspirationStripCount.Text = count == 0
                ? ""
                : count.ToString(CultureInfo.InvariantCulture);
        }

        var hasItems = count > 0;
        if (InspirationEmpty is not null)
            InspirationEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        if (InspirationAddButton is not null)
            InspirationAddButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        if (InspirationGlanceHost is not null)
            InspirationGlanceHost.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        if (_nav == MainNav.Flow)
            RefreshInspirationFlow();
    }

    private void RefreshInspirationFlow()
    {
        if (InspirationFlowDays is null)
            return;

        InspirationFlowDays.Children.Clear();
        IReadOnlyList<InspirationFlowDay> days = Array.Empty<InspirationFlowDay>();
        var failed = false;
        if (_settings.HasVault)
        {
            try
            {
                days = InspirationFlow.Read(_settings, _date, DateOnly.FromDateTime(DateTime.Now));
            }
            catch (Exception ex)
            {
                failed = true;
                ShowStatus("没能读出灵感：" + ex.Message, InfoBarSeverity.Warning);
            }
        }

        var total = days.Sum(day => day.Items.Count);
        if (InspirationFlowEmpty is not null)
        {
            InspirationFlowEmpty.Text = failed
                ? "没能读出这几天的灵感。"
                : "这几天还没有灵感。点「记一句」写进这一天。";
            InspirationFlowEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var day in days)
        {
            var date = day.Date;
            InspirationFlowDays.Children.Add(InspirationFlowPresenter.DayCard(
                day,
                date == _date,
                () => OpenFlowDate(date),
                EditInspirations));
        }
    }

    private void OpenFlowDate(DateOnly date)
    {
        _nav = MainNav.Today;
        if (date == _date)
        {
            RebuildList();
            UpdateStats();
            RefreshOverdue();
            return;
        }

        SetDate(date);
    }

    private void EditInspirations() => Inspiration_Click(this, new RoutedEventArgs());

    private void FocusPrimary_Click(object sender, RoutedEventArgs e)
    {
        FocusPopup.Dismiss();
        if (_focus.Phase == FocusPhase.Idle)
            _focus.StartFocus();
        else if (_focus.IsPaused)
            _focus.Resume();
        else
            _focus.Pause();
        ArmFocusClock();
        RenderFocus();
    }

    private void FocusReset_Click(object sender, RoutedEventArgs e)
    {
        FocusPopup.Dismiss();
        _focus.Reset();
        ArmFocusClock();
        RenderFocus();
    }

    private void FocusPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_focus.Phase != FocusPhase.Idle)
            return;
        if (sender is not FrameworkElement { Tag: string tag })
            return;

        var parts = tag.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var focus)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rest))
        {
            return;
        }

        if (!_focus.Use(new FocusDuration(focus, rest)))
            return;

        _settings.FocusMinutes = _focus.Duration.FocusMinutes;
        _settings.BreakMinutes = _focus.Duration.BreakMinutes;
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            ShowStatus("时长已换上，但没能记到本机：" + ex.Message, InfoBarSeverity.Warning);
        }

        RenderFocus();
    }

    private void ArmFocusClock()
    {
        _focusStamp = DateTime.UtcNow;
        if (_focusTimer is null)
            return;
        if (_focus.IsRunning)
            _focusTimer.Start();
        else
            _focusTimer.Stop();
    }

    private void OnFocusTick()
    {
        if (_closed)
        {
            _focusTimer?.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = now - _focusStamp;
        _focusStamp = now;
        var before = _focus.RemainingSeconds;
        FocusSignal signal;
        try
        {
            signal = _focus.Advance(elapsed);
        }
        catch
        {
            return;
        }

        if (signal == FocusSignal.None && before == _focus.RemainingSeconds)
            return;

        RenderFocus();
        if (!_focus.IsRunning)
            _focusTimer?.Stop();
        if (signal == FocusSignal.None)
            return;

        try
        {
            ShowFocusEnded(signal);
        }
        catch
        {
            // The corner card is optional. The clock has already returned to idle.
        }
    }

    private void ShowFocusEnded(FocusSignal signal)
    {
        var duration = _focus.Duration;
        if (signal == FocusSignal.FocusEnded)
        {
            FocusPopup.Show(
                FocusCopy.FocusEndedTitle,
                FocusCopy.FocusEndedBody(duration),
                new FocusPrompt("开始休息", true, BeginBreak),
                new FocusPrompt("再来一轮", false, BeginFocus),
                new FocusPrompt("关闭", false, null));
            return;
        }

        FocusPopup.Show(
            FocusCopy.BreakEndedTitle,
            FocusCopy.BreakEndedBody(duration),
            new FocusPrompt("开始专注", true, BeginFocus),
            new FocusPrompt("关闭", false, null));
    }

    private void BeginFocus()
    {
        if (_closed)
            return;
        _focus.StartFocus();
        ArmFocusClock();
        RenderFocus();
    }

    private void BeginBreak()
    {
        if (_closed)
            return;
        _focus.StartBreak();
        ArmFocusClock();
        RenderFocus();
    }

    private void RenderFocus()
    {
        if (FocusClock is null || FocusPhaseText is null || FocusPrimaryButton is null)
            return;

        FocusClock.Text = FocusSession.FormatClock(_focus.RemainingSeconds);
        FocusPhaseText.Text = FocusCopy.Status(_focus.Phase, _focus.IsPaused, _focus.CompletedRounds);
        var primary = FocusCopy.Primary(_focus.Phase, _focus.IsPaused);
        FocusPrimaryButton.Content = primary;
        FocusPrimaryButton.SetValue(AutomationProperties.NameProperty, primary);
        PaintPreset(FocusPreset25, 25, 5);
        PaintPreset(FocusPreset15, 15, 3);
        PaintPreset(FocusPreset50, 50, 10);
    }

    private void PaintPreset(Button? button, int focus, int rest)
    {
        if (button is null)
            return;

        var selected = _focus.Duration.FocusMinutes == focus && _focus.Duration.BreakMinutes == rest;
        button.IsEnabled = _focus.Phase == FocusPhase.Idle;
        if (!selected || Application.Current is null)
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            return;
        }

        if (Application.Current.Resources.TryGetValue("SoftAmberBrush", out var background) && background is Brush backgroundBrush)
            button.Background = backgroundBrush;
        if (Application.Current.Resources.TryGetValue("SoftAmberInkBrush", out var foreground) && foreground is Brush foregroundBrush)
            button.Foreground = foregroundBrush;
    }

    private void BindReminders()
    {
        IReadOnlyList<TaskReminder> all;
        try
        {
            all = ReminderStore.Load(ReminderStore.DefaultPath);
        }
        catch
        {
            all = Array.Empty<TaskReminder>();
        }

        var pool = all.Where(item => !item.Fired && item.Date == _date).ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _rows)
        {
            TaskReminder? match = null;
            if (!string.IsNullOrEmpty(row.ReminderId))
                match = pool.FirstOrDefault(item => item.Id == row.ReminderId);
            match ??= pool.FirstOrDefault(item =>
                !used.Contains(item.Id)
                && string.Equals(item.TaskText, (row.Text ?? "").Trim(), StringComparison.Ordinal));

            if (match is null || !used.Add(match.Id))
            {
                row.ReminderId = null;
                row.ReminderTime = null;
                continue;
            }

            row.ReminderId = match.Id;
            row.ReminderTime = match.Time;
        }
    }

    private void ForgetReminder(TaskRow row)
    {
        if (string.IsNullOrEmpty(row.ReminderId))
            return;

        try
        {
            ReminderStore.Remove(ReminderStore.DefaultPath, row.ReminderId);
        }
        catch (Exception ex)
        {
            ShowStatus("没能删除提醒：" + ex.Message, InfoBarSeverity.Warning);
        }

        row.ReminderId = null;
        row.ReminderTime = null;
    }

    private void SyncReminderText(TaskRow row)
    {
        if (string.IsNullOrEmpty(row.ReminderId))
            return;

        try
        {
            if (ReminderStore.Rename(ReminderStore.DefaultPath, row.ReminderId, row.Text))
                return;
        }
        catch (Exception ex)
        {
            ShowStatus("没能更新提醒：" + ex.Message, InfoBarSeverity.Warning);
        }
    }

    private static TaskReminder CopyReminder(TaskReminder reminder) => new()
    {
        Id = reminder.Id,
        TaskText = reminder.TaskText,
        Date = reminder.Date,
        Time = reminder.Time,
        Fired = reminder.Fired
    };

    private sealed class InspirationDraft
    {
        public InspirationDraft(string? timeLabel, string text, string? rawLine)
        {
            TimeLabel = timeLabel;
            Text = text;
            RawLine = rawLine;
        }

        public string? TimeLabel { get; }

        public string Text { get; set; }

        public string? RawLine { get; }
    }

    private void PushSuppress() => _suppressCount++;

    private void PopSuppress() => _suppressCount = Math.Max(0, _suppressCount - 1);

    private static CultureInfo CreateCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo("zh-CN");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
