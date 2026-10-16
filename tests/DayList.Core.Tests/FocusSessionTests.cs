using DayList.Core;

namespace DayList.Core.Tests;

public class FocusSessionTests
{
    [Fact]
    public void StartPauseResumeAndReset_StayOnOneClock()
    {
        var session = new FocusSession();
        Assert.Equal("25:00", FocusSession.FormatClock(session.RemainingSeconds));
        Assert.Equal("开始", FocusCopy.Primary(session.Phase, session.IsPaused));

        session.StartFocus();
        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(25 * 60 - 1, session.RemainingSeconds);
        Assert.Equal("专注中", FocusCopy.Phase(session.Phase, session.IsPaused));
        Assert.Equal("暂停", FocusCopy.Primary(session.Phase, session.IsPaused));

        session.Pause();
        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromSeconds(30)));
        Assert.Equal(25 * 60 - 1, session.RemainingSeconds);
        Assert.Equal("继续", FocusCopy.Primary(session.Phase, session.IsPaused));

        session.Resume();
        session.Reset();
        Assert.Equal(FocusPhase.Idle, session.Phase);
        Assert.Equal(25 * 60, session.RemainingSeconds);
        Assert.Equal("待开始", FocusCopy.Phase(session.Phase, session.IsPaused));
    }

    [Fact]
    public void Advance_FinishesOnePhaseAndDoesNotStartTheNext()
    {
        var session = new FocusSession();
        Assert.True(session.Use(FocusPresets.Short));
        session.StartFocus();

        var signal = session.Advance(TimeSpan.FromMinutes(40));

        Assert.Equal(FocusSignal.FocusEnded, signal);
        Assert.Equal(FocusPhase.Idle, session.Phase);
        Assert.Equal(1, session.CompletedRounds);
        Assert.Equal(15 * 60, session.RemainingSeconds);
        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromMinutes(10)));
        Assert.Equal("待开始 · 1 轮", FocusCopy.Status(session.Phase, session.IsPaused, session.CompletedRounds));
        Assert.Equal("这一轮 15 分钟结束了。可以休息 3 分钟，或再来一轮。", FocusCopy.FocusEndedBody(session.Duration));

        session.StartBreak();
        Assert.Equal(FocusSignal.BreakEnded, session.Advance(TimeSpan.FromMinutes(3)));
        Assert.Equal(1, session.CompletedRounds);
        Assert.Equal(FocusPhase.Idle, session.Phase);
        Assert.Equal("休息结束了。要再专注 15 分钟吗？", FocusCopy.BreakEndedBody(session.Duration));
    }

    [Fact]
    public void Advance_BanksSubsecondTicksAndDropsThemOnPause()
    {
        var session = new FocusSession();
        session.StartFocus();
        var remaining = session.RemainingSeconds;

        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(remaining, session.RemainingSeconds);
        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(remaining - 1, session.RemainingSeconds);

        session.Pause();
        session.Resume();
        Assert.Equal(FocusSignal.None, session.Advance(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(remaining - 1, session.RemainingSeconds);
    }

    [Fact]
    public void Use_IsIgnoredWhileAPhaseIsRunning()
    {
        var session = new FocusSession();
        session.StartFocus();
        session.Advance(TimeSpan.FromSeconds(5));

        Assert.False(session.Use(FocusPresets.Long));
        Assert.Equal(25, session.Duration.FocusMinutes);
        Assert.Equal(25 * 60 - 5, session.RemainingSeconds);
    }

    [Fact]
    public void Normalize_RejectsEmptyOrHugeDurations()
    {
        Assert.Equal(FocusPresets.Classic, FocusDuration.Normalize(0, 0));
        Assert.Equal(FocusPresets.Classic, FocusDuration.Normalize(500, -2));
        Assert.Equal(new FocusDuration(50, 10), FocusDuration.Normalize(50, 10));
    }

    [Fact]
    public void Settings_RememberTheChosenDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-focus-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings { FocusMinutes = 50, BreakMinutes = 10 };
            var copy = settings.Copy();
            Assert.Equal(50, copy.FocusMinutes);
            Assert.Equal(10, copy.BreakMinutes);

            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);
            Assert.Equal(50, loaded.FocusMinutes);
            Assert.Equal(10, loaded.BreakMinutes);

            File.WriteAllText(path, """{ "focusMinutes": 0, "breakMinutes": 90 }""");
            var repaired = SettingsStore.Load(path);
            Assert.Equal(25, repaired.FocusMinutes);
            Assert.Equal(5, repaired.BreakMinutes);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Corner_SitsOnTheLeftAndLeavesTheReminderCornerFree()
    {
        var work = new PixelRect(0, 0, 1920, 1080);
        var focus = FocusCorner.Place(work, 360, 188);
        var reminder = ReminderCorner.Place(work, 380, 196, 0);

        Assert.Equal(16, focus.X);
        Assert.Equal(1080 - 188 - 16, focus.Y);
        Assert.True(focus.X + focus.Width < reminder.X);
    }
}
