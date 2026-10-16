namespace DayList.Core;

public enum FocusPhase
{
    Idle,
    Focus,
    Break
}

public enum FocusSignal
{
    None,
    FocusEnded,
    BreakEnded
}

public readonly record struct FocusDuration(int FocusMinutes, int BreakMinutes)
{
    public static FocusDuration Normalize(int focusMinutes, int breakMinutes)
    {
        var focus = focusMinutes is >= 1 and <= 120 ? focusMinutes : 25;
        var rest = breakMinutes is >= 1 and <= 60 ? breakMinutes : 5;
        return new FocusDuration(focus, rest);
    }

    public int FocusSeconds => FocusMinutes * 60;

    public int BreakSeconds => BreakMinutes * 60;
}

public static class FocusPresets
{
    public static FocusDuration Classic { get; } = new(25, 5);

    public static FocusDuration Short { get; } = new(15, 3);

    public static FocusDuration Long { get; } = new(50, 10);
}

/// <summary>
/// In-memory pomodoro. A tick finishes at most one phase and never starts the next by itself.
/// </summary>
public sealed class FocusSession
{
    private int _bankMs;

    public FocusPhase Phase { get; private set; } = FocusPhase.Idle;

    public bool IsPaused { get; private set; }

    public bool IsRunning => Phase != FocusPhase.Idle && !IsPaused;

    public int RemainingSeconds { get; private set; } = FocusPresets.Classic.FocusSeconds;

    public int CompletedRounds { get; private set; }

    public FocusDuration Duration { get; private set; } = FocusPresets.Classic;

    public bool Use(FocusDuration duration)
    {
        if (Phase != FocusPhase.Idle)
            return false;

        Duration = FocusDuration.Normalize(duration.FocusMinutes, duration.BreakMinutes);
        RemainingSeconds = Duration.FocusSeconds;
        _bankMs = 0;
        return true;
    }

    public void StartFocus()
    {
        Phase = FocusPhase.Focus;
        IsPaused = false;
        RemainingSeconds = Duration.FocusSeconds;
        _bankMs = 0;
    }

    public void StartBreak()
    {
        Phase = FocusPhase.Break;
        IsPaused = false;
        RemainingSeconds = Duration.BreakSeconds;
        _bankMs = 0;
    }

    public void Pause()
    {
        if (!IsRunning)
            return;

        IsPaused = true;
        _bankMs = 0;
    }

    public void Resume()
    {
        if (Phase == FocusPhase.Idle || !IsPaused)
            return;

        IsPaused = false;
        _bankMs = 0;
    }

    public void Reset()
    {
        Phase = FocusPhase.Idle;
        IsPaused = false;
        RemainingSeconds = Duration.FocusSeconds;
        _bankMs = 0;
    }

    public FocusSignal Advance(TimeSpan elapsed)
    {
        if (!IsRunning)
            return FocusSignal.None;

        var add = elapsed.TotalMilliseconds;
        if (double.IsNaN(add) || add <= 0)
            return FocusSignal.None;
        if (add > 86_400_000d)
            add = 86_400_000d;

        var next = (long)_bankMs + (long)add;
        if (next > int.MaxValue)
            next = int.MaxValue;
        _bankMs = (int)next;

        var whole = _bankMs / 1000;
        if (whole <= 0)
            return FocusSignal.None;

        if (whole < RemainingSeconds)
        {
            RemainingSeconds -= whole;
            _bankMs %= 1000;
            return FocusSignal.None;
        }

        return FinishPhase();
    }

    public static string FormatClock(int totalSeconds)
    {
        if (totalSeconds < 0)
            totalSeconds = 0;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private FocusSignal FinishPhase()
    {
        var ended = Phase;
        Phase = FocusPhase.Idle;
        IsPaused = false;
        _bankMs = 0;
        RemainingSeconds = Duration.FocusSeconds;
        if (ended == FocusPhase.Focus)
        {
            CompletedRounds++;
            return FocusSignal.FocusEnded;
        }

        return FocusSignal.BreakEnded;
    }
}

public static class FocusCopy
{
    public static string Phase(FocusPhase phase, bool paused) => phase switch
    {
        FocusPhase.Focus => paused ? "已暂停" : "专注中",
        FocusPhase.Break => paused ? "已暂停" : "休息中",
        _ => "待开始"
    };

    public static string Status(FocusPhase phase, bool paused, int rounds)
    {
        var label = Phase(phase, paused);
        return rounds > 0 ? $"{label} · {rounds} 轮" : label;
    }

    public static string Primary(FocusPhase phase, bool paused) => phase switch
    {
        FocusPhase.Idle => "开始",
        _ when paused => "继续",
        _ => "暂停"
    };

    public const string FocusEndedTitle = "专注结束";

    public const string BreakEndedTitle = "休息结束";

    public static string FocusEndedBody(FocusDuration duration) =>
        $"这一轮 {duration.FocusMinutes} 分钟结束了。可以休息 {duration.BreakMinutes} 分钟，或再来一轮。";

    public static string BreakEndedBody(FocusDuration duration) =>
        $"休息结束了。要再专注 {duration.FocusMinutes} 分钟吗？";
}

/// <summary>
/// Bottom-left corner, so a focus card does not sit on top of reminder cards.
/// </summary>
public static class FocusCorner
{
    public static PixelRect Place(PixelRect work, int width, int height)
    {
        if (width < 1)
            width = 1;
        if (height < 1)
            height = 1;
        if (width > work.Width)
            width = Math.Max(1, work.Width);
        if (height > work.Height)
            height = Math.Max(1, work.Height);

        var y = ReminderCorner.Place(work, width, height, 0).Y;
        var x = work.X + ReminderCorner.Margin;
        if (x + width > work.X + work.Width)
            x = work.X + work.Width - width;
        if (x < work.X)
            x = work.X;
        return new PixelRect(x, y, width, height);
    }
}
