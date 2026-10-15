using System.Text.RegularExpressions;
using DayList.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DayList;

/// <summary>
/// Windows toast for a due reminder. Unpackaged registration can fail; callers fall back to the in-app dialog.
/// </summary>
internal static class ReminderNotifier
{
    private static readonly Regex ArgumentPart = new(
        @"^(?<key>[^=;&]+)=(?<value>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool _registered;

    public static event Action<string, string>? Activated;

    public static void EnsureRegistered()
    {
        if (_registered)
            return;

        try
        {
            if (!AppNotificationManager.IsSupported())
                return;

            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            try
            {
                AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            }
            catch
            {
                // Register failed before the handler was usable.
            }

            _registered = false;
        }
    }

    public static void Unregister()
    {
        if (!_registered)
            return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
            // Best-effort. The process is exiting.
        }

        _registered = false;
    }

    public static bool TryShow(TaskReminder reminder, bool sound)
    {
        try
        {
            EnsureRegistered();
            if (!_registered)
                return false;

            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddArgument("id", reminder.Id)
                .AddText("到点了")
                .AddText(reminder.TaskText)
                .AddButton(new AppNotificationButton("稍后 10 分钟")
                    .AddArgument("action", "snooze")
                    .AddArgument("id", reminder.Id))
                .AddButton(new AppNotificationButton("打开 DayList")
                    .AddArgument("action", "open")
                    .AddArgument("id", reminder.Id));

            if (sound)
                builder.SetAudioEvent(AppNotificationSoundEvent.Reminder);
            else
                builder.MuteAudio();

            AppNotificationManager.Default.Show(builder.BuildNotification());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args.Arguments is not null)
        {
            foreach (var pair in args.Arguments)
                map[pair.Key] = pair.Value;
        }

        foreach (var pair in ParseArguments(args.Argument))
            map.TryAdd(pair.Key, pair.Value);

        map.TryGetValue("action", out var action);
        map.TryGetValue("id", out var id);
        if (string.IsNullOrWhiteSpace(id))
            return;

        Activated?.Invoke(string.IsNullOrWhiteSpace(action) ? "open" : action, id);
    }

    internal static Dictionary<string, string> ParseArguments(string? argument)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(argument))
            return map;

        foreach (var part in argument.Split(new[] { ';', '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ArgumentPart.Match(part.Trim());
            if (!match.Success)
                continue;
            map[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        return map;
    }
}
