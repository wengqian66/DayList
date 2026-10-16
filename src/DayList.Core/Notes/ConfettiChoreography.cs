namespace DayList.Core;

/// <summary>
/// A single scrap in the day-complete sprinkle. Positions are fractions of the
/// overlay (so the burst does not depend on a layout pass having a storyboard
/// target). Sizes are device-independent pixels.
/// </summary>
public readonly record struct ConfettiPiece(
    float X,
    float Y,
    float Drift,
    float Fall,
    float Width,
    float Height,
    float StartDegrees,
    float EndDegrees,
    byte R,
    byte G,
    byte B,
    int DelayMs);

public readonly record struct ConfettiTint(byte R, byte G, byte B);

/// <summary>
/// Small pastel paper scraps for the moment the last open task of the day is
/// checked. A finished category does not use this — that moment stays a banner.
/// </summary>
public static class ConfettiChoreography
{
    public const int PieceCount = 28;
    public const int MotionMs = 1280;
    public const int MaxDelayMs = 140;
    public const int LifetimeMs = MotionMs + MaxDelayMs;

    public static readonly ConfettiTint[] Palette =
    [
        new(0xF0, 0xC2, 0xA8),
        new(0xA9, 0xC8, 0xE6),
        new(0xB7, 0xD0, 0xBE),
        new(0xF0, 0xC2, 0xCC),
        new(0xE6, 0xD3, 0xA8),
        new(0xD4, 0xC6, 0xE6),
        new(0xF2, 0xD0, 0xB0)
    ];

    public static bool ForDay(CompletionMoment moment) => moment.DayJustCompleted;

    public static IReadOnlyList<ConfettiPiece> Pieces(int seed)
    {
        var random = new Random(seed);
        var pieces = new ConfettiPiece[PieceCount];
        for (var i = 0; i < pieces.Length; i++)
        {
            var tint = Palette[random.Next(Palette.Length)];
            var x = 0.18f + (float)random.NextDouble() * 0.64f;
            var y = 0.02f + (float)random.NextDouble() * 0.06f;
            var drift = ((float)random.NextDouble() - 0.5f) * 0.14f;
            var fall = 0.38f + (float)random.NextDouble() * 0.26f;
            var wide = random.NextDouble() < 0.25;
            var width = wide
                ? 8.5f + (float)random.NextDouble() * 3f
                : 6f + (float)random.NextDouble() * 3.2f;
            var height = 4f + (float)random.NextDouble() * 2.6f;
            var start = ((float)random.NextDouble() - 0.5f) * 70f;
            var end = start + ((float)random.NextDouble() - 0.5f) * 200f;
            var delay = random.Next(0, MaxDelayMs + 1);
            pieces[i] = new ConfettiPiece(
                x, y, drift, fall, width, height, start, end, tint.R, tint.G, tint.B, delay);
        }

        return pieces;
    }
}
