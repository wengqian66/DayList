using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DayList.Core;
using Windows.UI;

namespace DayList;

public sealed class TaskRow : INotifyPropertyChanged
{
    private bool _isDone;
    private string _text = "";
    private string _category = DayTaskCategory.UncategorizedKey;
    private string _indent = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value)
                return;
            _isDone = value;
            Notify();
            Notify(nameof(TextOpacity));
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;
            _text = value;
            Notify();
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            var normalized = DayTaskCategory.Normalize(value);
            if (_category == normalized)
                return;
            _category = normalized;
            Notify();
            Notify(nameof(CategoryLabel));
        }
    }

    public string CategoryLabel => DayTaskCategory.Display(_category);

    public string Indent
    {
        get => _indent;
        set
        {
            value ??= "";
            if (_indent == value)
                return;
            _indent = value;
            Notify();
            Notify(nameof(IndentMargin));
            Notify(nameof(IndentLevel));
            Notify(nameof(IndentWidth));
        }
    }

    public int IndentLevel => DayTask.MeasureIndent(_indent);

    public Thickness IndentMargin => new(12 + IndentLevel * 18, 0, 0, 0);

    public double IndentWidth => IndentLevel * 18;

    public char Bullet { get; set; } = '-';

    public char DoneMark { get; set; } = 'x';

    public string? RawLine { get; set; }

    private string? _reminderId;
    private string? _reminderTime;

    public string? ReminderId
    {
        get => _reminderId;
        set
        {
            if (_reminderId == value)
                return;
            _reminderId = value;
            Notify();
        }
    }

    public string? ReminderTime
    {
        get => _reminderTime;
        set
        {
            if (_reminderTime == value)
                return;
            _reminderTime = value;
            Notify();
            Notify(nameof(ReminderToolTip));
            Notify(nameof(ReminderTimeVisibility));
            Notify(nameof(ReminderIconVisibility));
        }
    }

    public string ReminderToolTip => string.IsNullOrEmpty(_reminderTime)
        ? "滚动选择这一天的时间，开着或在托盘才会响。"
        : $"{_reminderTime}，点击可改时间或清除。";

    public Visibility ReminderTimeVisibility =>
        string.IsNullOrEmpty(_reminderTime) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ReminderIconVisibility =>
        string.IsNullOrEmpty(_reminderTime) ? Visibility.Visible : Visibility.Collapsed;

    private bool _isDropTarget;

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
                return;
            _isDropTarget = value;
            Notify();
            Notify(nameof(RowBackground));
        }
    }

    public Brush RowBackground => _isDropTarget ? DropBrush : ClearBrush;

    private static readonly SolidColorBrush DropBrush = new(Color.FromArgb(48, 0x5B, 0x9B, 0xD5));

    private static readonly SolidColorBrush ClearBrush = new(Color.FromArgb(0, 0, 0, 0));

    public int OriginIndex { get; set; } = -1;

    public double TextOpacity => IsDone ? 0.5 : 1;

    public static TaskRow From(DayTask task) => new()
    {
        IsDone = task.IsDone,
        Text = task.Text,
        Category = task.Category,
        Indent = task.Indent,
        Bullet = task.Bullet,
        DoneMark = task.DoneMark,
        RawLine = task.RawLine,
        OriginIndex = task.OriginIndex
    };

    public DayTask ToTask() => new()
    {
        IsDone = IsDone,
        Text = Text,
        Category = Category,
        Indent = Indent,
        Bullet = Bullet,
        DoneMark = DoneMark,
        RawLine = RawLine,
        OriginIndex = OriginIndex
    };

    public void IndentIn()
    {
        Indent = DayTask.IndentForLevel(IndentLevel + 1);
        RawLine = null;
    }

    public void IndentOut()
    {
        Indent = DayTask.IndentForLevel(Math.Max(0, IndentLevel - 1));
        RawLine = null;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CategoryAccent
{
    private static readonly (string Name, byte R, byte G, byte B, string Glyph)[] Known =
    {
        ("工作", 0x5B, 0x9B, 0xD5, "\uE821"),
        ("学习", 0x6B, 0xB5, 0x6C, "\uE8F1"),
        ("生活", 0x9B, 0x7E, 0xBD, "\uE80F"),
        ("健康", 0xE8, 0x8B, 0x7D, "\uE95E"),
        ("运动", 0xE8, 0x8B, 0x7D, "\uE95E"),
    };

    private static readonly (byte R, byte G, byte B)[] Palette =
    {
        (0x5B, 0x9B, 0xD5),
        (0x6B, 0xB5, 0x6C),
        (0x9B, 0x7E, 0xBD),
        (0xE8, 0xA0, 0x6F),
        (0x5D, 0xAE, 0xC8),
        (0xD4, 0x8B, 0xA0),
        (0x8F, 0xA3, 0x6A),
        (0x7A, 0x8F, 0xC7),
    };

    public static Color For(string? category)
    {
        var name = DayTaskCategory.Display(category);
        foreach (var item in Known)
        {
            if (string.Equals(item.Name, name, StringComparison.Ordinal))
                return Color.FromArgb(255, item.R, item.G, item.B);
        }

        if (DayTaskCategory.IsUncategorized(category))
            return Color.FromArgb(255, 0xA8, 0xA0, 0x96);

        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in name)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            var tone = Palette[hash % (uint)Palette.Length];
            return Color.FromArgb(255, tone.R, tone.G, tone.B);
        }
    }

    public static string Glyph(string? category)
    {
        var name = DayTaskCategory.Display(category);
        foreach (var item in Known)
        {
            if (string.Equals(item.Name, name, StringComparison.Ordinal))
                return item.Glyph;
        }

        return DayTaskCategory.IsUncategorized(category) ? "\uE8FD" : "\uE8EC";
    }

    public static SolidColorBrush Brush(string? category) => new(For(category));

    public static SolidColorBrush SoftBrush(string? category)
    {
        var c = For(category);
        return new SolidColorBrush(Color.FromArgb(48, c.R, c.G, c.B));
    }

    /// <summary>Soft pastel wash for the whole category card body.</summary>
    public static SolidColorBrush CardFill(string? category)
    {
        var c = For(category);
        return new SolidColorBrush(Color.FromArgb(56, c.R, c.G, c.B));
    }

    public static SolidColorBrush HeaderWash(string? category)
    {
        var c = For(category);
        return new SolidColorBrush(Color.FromArgb(72, c.R, c.G, c.B));
    }
}

public sealed class CategoryCardModel : INotifyPropertyChanged
{
    private int _doneCount;
    private int _totalCount;

    public CategoryCardModel(string category)
    {
        Category = DayTaskCategory.Normalize(category);
        var accent = CategoryAccent.For(Category);
        AccentBrush = new SolidColorBrush(accent);
        SoftAccentBrush = CategoryAccent.SoftBrush(Category);
        CardFillBrush = CategoryAccent.CardFill(Category);
        HeaderWashBrush = CategoryAccent.HeaderWash(Category);
        Glyph = CategoryAccent.Glyph(Category);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Category { get; }

    public string Title => DayTaskCategory.Display(Category);

    public string Glyph { get; }

    public SolidColorBrush AccentBrush { get; }

    public SolidColorBrush SoftAccentBrush { get; }

    public SolidColorBrush CardFillBrush { get; }

    public SolidColorBrush HeaderWashBrush { get; }

    public ObservableCollection<TaskRow> Tasks { get; } = new();

    private bool _isDropTarget;

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
                return;
            _isDropTarget = value;
            Notify();
            Notify(nameof(CardBorderBrush));
            Notify(nameof(CardBorderThickness));
        }
    }

    public Brush CardBorderBrush => _isDropTarget
        ? AccentBrush
        : (Brush)Application.Current.Resources["SoftLineBrush"];

    public Thickness CardBorderThickness => new(_isDropTarget ? 2 : 1);

    public int DoneCount
    {
        get => _doneCount;
        private set
        {
            if (_doneCount == value)
                return;
            _doneCount = value;
            Notify();
            Notify(nameof(FractionText));
            Notify(nameof(ProgressValue));
            Notify(nameof(ProgressPercent));
            Notify(nameof(ProgressLabel));
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (_totalCount == value)
                return;
            _totalCount = value;
            Notify();
            Notify(nameof(FractionText));
            Notify(nameof(ProgressValue));
            Notify(nameof(ProgressPercent));
            Notify(nameof(ProgressLabel));
            Notify(nameof(CountText));
        }
    }

    public string FractionText => TotalCount == 0 ? "0 / 0" : $"{DoneCount} / {TotalCount}";

    /// <summary>Item count for the completed list. Not a progress score.</summary>
    public string CountText => TotalCount == 0 ? "" : $"{TotalCount} 项";

    public double ProgressValue => TotalCount == 0 ? 0 : (double)DoneCount / TotalCount;

    public int ProgressPercent => TotalCount == 0 ? 0 : (int)Math.Round(ProgressValue * 100);

    public string ProgressLabel => $"{ProgressPercent}%";

    public void RefreshStats()
    {
        DoneCount = Tasks.Count(row => row.IsDone);
        TotalCount = Tasks.Count;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CategoryHeaderRow
{
    public CategoryHeaderRow(string category) => Category = DayTaskCategory.Normalize(category);

    public string Category { get; }

    public string Title => DayTaskCategory.Display(Category);
}

/// <summary>One saved inspiration, as shown on the Today card. Editing stays in the dialog.</summary>
public sealed class InspirationGlance
{
    public InspirationGlance(string timeLabel, string preview)
    {
        TimeLabel = string.IsNullOrEmpty(timeLabel) ? "·" : timeLabel;
        Preview = preview ?? "";
    }

    public string TimeLabel { get; }

    public string Preview { get; }
}

public sealed class OverdueRow
{
    public OverdueRow(OverdueTaskItem item)
    {
        Item = item;
        DateText = item.Date.ToString("M/d");
        Text = item.Text;
        CategoryLabel = DayTaskCategory.Display(item.Category);
    }

    public OverdueTaskItem Item { get; }

    public string DateText { get; }

    public string Text { get; }

    public string CategoryLabel { get; }

    public override string ToString() => "[" + DateText + "] " + Text + "  (点击移到今天)";
}

public sealed class ListItemTemplateSelector : Microsoft.UI.Xaml.Controls.DataTemplateSelector
{
    public Microsoft.UI.Xaml.DataTemplate? TaskTemplate { get; set; }

    public Microsoft.UI.Xaml.DataTemplate? HeaderTemplate { get; set; }

    protected override Microsoft.UI.Xaml.DataTemplate SelectTemplateCore(object item) =>
        item is CategoryHeaderRow
            ? HeaderTemplate!
            : TaskTemplate!;

    protected override Microsoft.UI.Xaml.DataTemplate SelectTemplateCore(object item, Microsoft.UI.Xaml.DependencyObject container) =>
        SelectTemplateCore(item);
}
