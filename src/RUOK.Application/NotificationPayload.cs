using System.Xml.Linq;
using RUOK.Domain;

namespace RUOK.Application;

public sealed record NotificationText(string Title, string Body, string Survey, string Skip, IReadOnlyList<string> Moods);

public static class NotificationPayload
{
    public static string CreateEncouragement(NotificationIntent intent, string title, string message)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EncouragementPreferences.ValidateMessage(message);
        if (intent.Action != NotificationAction.Encouragement || intent.Mood is not null)
            throw new ArgumentException("An encouragement must use a read-only activation.", nameof(intent));
        return new XElement("toast",
            new XAttribute("launch", intent.ToArguments()),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", title), new XElement("text", message))))
            .ToString(SaveOptions.DisableFormatting);
    }

    public static string Create(NotificationIntent intent, NotificationLayout layout, NotificationText text,
        IReadOnlyList<string>? imageUris = null)
    {
        if (text.Moods.Count != 5)
            throw new ArgumentException("Five accessible mood labels are required.", nameof(text));
        if (imageUris is not null && (imageUris.Count != 5 || imageUris.Any(value =>
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile || uri.IsUnc)))
            throw new ArgumentException("Five local-file image URIs are required for standalone notifications.", nameof(imageUris));
        var actions = new XElement("actions");
        if (layout == NotificationLayout.Faces)
        {
            for (var score = 1; score <= 5; score++)
                actions.Add(new XElement("action",
                    new XAttribute("content", ""),
                    new XAttribute("arguments", (intent with { Action = NotificationAction.Mood, Mood = (Mood)score }).ToArguments()),
                    new XAttribute("activationType", "foreground"),
                    new XAttribute("imageUri", imageUris?[score - 1] ?? $"ms-appx:///Assets/NotificationFaces/Mood{score}.png"),
                    new XAttribute("hint-toolTip", text.Moods[score - 1])));
        }
        else if (layout == NotificationLayout.SurveyAndSkip)
        {
            actions.Add(new XElement("action", new XAttribute("content", text.Survey),
                new XAttribute("arguments", (intent with { Action = NotificationAction.Open, Mood = null }).ToArguments()),
                new XAttribute("activationType", "foreground")));
            actions.Add(new XElement("action", new XAttribute("content", text.Skip),
                new XAttribute("arguments", (intent with { Action = NotificationAction.Skip, Mood = null }).ToArguments()),
                new XAttribute("activationType", "foreground")));
        }
        else
            throw new ArgumentOutOfRangeException(nameof(layout));

        return new XElement("toast",
            new XAttribute("launch", (intent with { Action = NotificationAction.Open, Mood = null }).ToArguments()),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", text.Title), new XElement("text", text.Body))),
            actions).ToString(SaveOptions.DisableFormatting);
    }
}
