using System.Globalization;
using RUOK.Domain;

namespace RUOK.Application;

public enum NotificationAction
{
    Open,
    Mood,
    Skip,
    Encouragement
}

public enum NotificationOutcome
{
    Expired,
    Skip,
    TestPreview,
    NeedsConsent,
    Disabled,
    Save,
    Open,
    Encouragement
}

public sealed record NotificationIntent(
    Guid Id, DateTimeOffset CreatedAt, NotificationAction Action, Mood? Mood, bool IsTest)
{
    public bool IsExpired(DateTimeOffset now) =>
        CreatedAt > now.AddMinutes(5) || now - CreatedAt > TimeSpan.FromHours(24);

    public NotificationOutcome Evaluate(AppSettings settings, DateTimeOffset now)
    {
        if (IsExpired(now))
            return NotificationOutcome.Expired;
        if (Action == NotificationAction.Encouragement)
            return NotificationOutcome.Encouragement;
        if (Action == NotificationAction.Skip)
            return NotificationOutcome.Skip;
        if (IsTest)
            return NotificationOutcome.TestPreview;
        if (!settings.PrivacyAccepted)
            return NotificationOutcome.NeedsConsent;
        if (settings.Notifications.Mode == ReminderMode.Disabled)
            return NotificationOutcome.Disabled;
        return Action == NotificationAction.Mood && Mood is not null
            && settings.Notifications.FaceAction == NotificationFaceAction.SaveMood
                ? NotificationOutcome.Save : NotificationOutcome.Open;
    }

    public string ToArguments() =>
        $"v=1&id={Id:N}&created={CreatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"
        + $"&action={Action.ToString().ToLowerInvariant()}&test={(IsTest ? "1" : "0")}"
        + (Mood is { } mood ? $"&mood={((int)mood).ToString(CultureInfo.InvariantCulture)}" : "");

    public static bool TryParse(string argument, out NotificationIntent? intent)
    {
        intent = null;
        if (string.IsNullOrEmpty(argument) || argument.Length > 1024)
            return false;
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var part in argument.Split('&'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
                return false;
            pairs.Add(new KeyValuePair<string, string>(part[..separator], part[(separator + 1)..]));
        }
        return TryParse(pairs, out intent);
    }

    public static bool TryParse(IEnumerable<KeyValuePair<string, string>> arguments, out NotificationIntent? intent)
    {
        intent = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in arguments)
            if (!values.TryAdd(pair.Key, pair.Value))
                return false;
        if (!values.TryGetValue("v", out var version) || version != "1"
            || !values.TryGetValue("id", out var rawId) || !Guid.TryParseExact(rawId, "N", out var id) || id == Guid.Empty
            || !values.TryGetValue("created", out var rawTime)
            || !long.TryParse(rawTime, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || timestamp is < 0 or > 253402300799
            || !values.TryGetValue("action", out var rawAction)
            || !values.TryGetValue("test", out var rawTest) || rawTest is not ("0" or "1"))
            return false;
        NotificationAction? action = rawAction switch
        {
            "open" => NotificationAction.Open,
            "mood" => NotificationAction.Mood,
            "skip" => NotificationAction.Skip,
            "encouragement" => NotificationAction.Encouragement,
            _ => null
        };
        if (action is null)
            return false;
        if (action == NotificationAction.Encouragement && values.ContainsKey("mood"))
            return false;
        Mood? mood = null;
        if (action == NotificationAction.Mood)
        {
            if (!values.TryGetValue("mood", out var rawMood)
                || !int.TryParse(rawMood, NumberStyles.None, CultureInfo.InvariantCulture, out var score)
                || score is < 1 or > 5)
                return false;
            mood = (Mood)score;
        }
        intent = new NotificationIntent(id, DateTimeOffset.FromUnixTimeSeconds(timestamp), action.Value, mood, rawTest == "1");
        return true;
    }
}
