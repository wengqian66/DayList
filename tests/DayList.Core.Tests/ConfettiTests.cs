using DayList.Core;

namespace DayList.Core.Tests;

public class ConfettiTests
{
    [Fact]
    public void Confetti_OnlyWhenTheDayJustCompleted()
    {
        var day = CompletionFeedback.Detect(
            new[]
            {
                new TaskDoneSnapshot("工作", true),
                new TaskDoneSnapshot("生活", false)
            },
            new[]
            {
                new TaskDoneSnapshot("工作", true),
                new TaskDoneSnapshot("生活", true)
            });
        Assert.True(day.DayJustCompleted);
        Assert.True(ConfettiChoreography.ForDay(day));

        var category = CompletionFeedback.Detect(
            new[]
            {
                new TaskDoneSnapshot("工作", false),
                new TaskDoneSnapshot("生活", false)
            },
            new[]
            {
                new TaskDoneSnapshot("工作", true),
                new TaskDoneSnapshot("生活", false)
            });
        Assert.False(category.DayJustCompleted);
        Assert.False(ConfettiChoreography.ForDay(category));
        Assert.False(ConfettiChoreography.ForDay(default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1109)]
    [InlineData(20261016)]
    [InlineData(-7)]
    public void Pieces_AreAShortPastelSprinkle(int seed)
    {
        Assert.InRange(ConfettiChoreography.LifetimeMs, 1000, 1500);
        Assert.InRange(ConfettiChoreography.PieceCount, 20, 36);

        var pieces = ConfettiChoreography.Pieces(seed);
        Assert.Equal(ConfettiChoreography.PieceCount, pieces.Count);

        foreach (var piece in pieces)
        {
            Assert.InRange(piece.X, 0.17f, 0.83f);
            Assert.InRange(piece.Y, 0.01f, 0.09f);
            Assert.InRange(piece.Drift, -0.08f, 0.08f);
            Assert.InRange(piece.Fall, 0.37f, 0.65f);
            Assert.InRange(piece.X + piece.Drift, 0.09f, 0.91f);
            Assert.InRange(piece.Y + piece.Fall, 0.38f, 0.74f);
            Assert.InRange(piece.Width, 5.5f, 12f);
            Assert.InRange(piece.Height, 3.8f, 7f);
            Assert.InRange(piece.StartDegrees, -40f, 40f);
            Assert.InRange(Math.Abs(piece.EndDegrees - piece.StartDegrees), 0f, 110f);
            Assert.InRange(piece.DelayMs, 0, ConfettiChoreography.MaxDelayMs);
            Assert.Contains(ConfettiChoreography.Palette, tint =>
                tint.R == piece.R && tint.G == piece.G && tint.B == piece.B);
            Assert.True(Saturation(piece.R, piece.G, piece.B) < 0.45);
            Assert.True(piece.R >= 160 && piece.G >= 160 && piece.B >= 160);
        }
    }

    private static double Saturation(byte r, byte g, byte b)
    {
        var rf = r / 255d;
        var gf = g / 255d;
        var bf = b / 255d;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        if (max == 0)
            return 0;
        return (max - min) / max;
    }
}
