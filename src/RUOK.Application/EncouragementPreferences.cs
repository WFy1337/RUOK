using System.Xml;

namespace RUOK.Application;

public sealed record EncouragementPreferences
{
    public const int MaximumMessages = 30;
    public const int MaximumMessageLength = 160;
    public const int MaximumTextLength = MaximumMessages * (MaximumMessageLength + 2);
    public const string DefaultMessages = """
        Keep it going!
        You've got this, one step at a time.
        Small steps count.
        Your effort matters.
        One thing at a time is enough.
        Be kind to yourself today.
        Take a breath. You can begin again.
        A small pause can make room for a fresh start.
        Progress can happen at your own pace.
        Give yourself credit for showing up.
        You deserve a moment to recharge.
        Keep making room for what matters to you.
        """;

    public bool Enabled { get; init; }
    public int MinimumIntervalMinutes { get; init; } = 120;
    public string Messages { get; init; } = DefaultMessages;

    public void Validate()
    {
        if (MinimumIntervalMinutes is < 15 or > 240)
            throw new ArgumentOutOfRangeException(nameof(MinimumIntervalMinutes),
                "Use a minimum spacing between 15 and 240 minutes.");
        GetMessages();
    }

    public IReadOnlyList<string> GetMessages()
    {
        ArgumentNullException.ThrowIfNull(Messages);
        if (Messages.Length > MaximumTextLength)
            throw new ArgumentException("The message list is too long.", nameof(Messages));
        var lines = Messages.Split(["\r\n", "\n", "\r"],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (lines.Length is < 1 or > MaximumMessages)
            throw new ArgumentException("Use between 1 and 30 messages, one per line.", nameof(Messages));
        foreach (var line in lines)
            ValidateMessage(line);
        return lines;
    }

    public EncouragementPreferences Normalize()
    {
        Validate();
        return this with { Messages = string.Join("\n", GetMessages()) };
    }

    public static void ValidateMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > MaximumMessageLength || message.Any(char.IsControl))
            throw new ArgumentException("Each message must be a single line of at most 160 characters.", nameof(message));
        try
        {
            XmlConvert.VerifyXmlChars(message);
        }
        catch (XmlException error)
        {
            throw new ArgumentException("A message contains unsupported text characters.", nameof(message), error);
        }
    }
}

public sealed record EncouragementState
{
    public EncouragementPreferences Preferences { get; init; } = new();
    public DateTimeOffset? NextUtc { get; init; }
    public string? LastMessage { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Preferences);
        Preferences.Validate();
        if (NextUtc is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new ArgumentException("The next encouragement time must be UTC.", nameof(NextUtc));
        if (!Preferences.Enabled && (NextUtc is not null || LastMessage is not null))
            throw new ArgumentException("Disabled encouragements must not retain an active schedule.");
        if (LastMessage is not null)
        {
            EncouragementPreferences.ValidateMessage(LastMessage);
            if (!Preferences.GetMessages().Contains(LastMessage, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("The previous message must belong to the saved list.", nameof(LastMessage));
        }
    }
}
