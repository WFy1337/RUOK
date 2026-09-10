using System.Globalization;
using System.Text;
using RUOK.Application;
using RUOK.Domain;

namespace RUOK.Infrastructure;

public sealed class CsvExporter : ICsvExporter
{
    public static readonly IReadOnlyList<string> Columns = Array.AsReadOnly(new[]
    {
        "entry_id", "timestamp_local", "timestamp_utc", "utc_offset_minutes", "time_zone_id",
        "pulsecheck_type", "mood_score", "mood_label", "stress_score", "energy_score",
        "influencing_factors", "schema_version"
    });

    public async Task ExportAsync(
        string path, IReadOnlyList<PulseEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);
        if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a file with a .csv extension.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".ruok-export-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await WriteAsync(stream, entries, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Stage beside the destination so a failed or cancelled write does not replace an existing export.
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static async Task WriteAsync(
        Stream stream, IReadOnlyList<PulseEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(entries);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, leaveOpen: true)
        {
            NewLine = "\r\n"
        };
        await writer.WriteLineAsync(string.Join(",", Columns).AsMemory(), cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries.OrderBy(entry => entry.RecordedAt).ThenBy(entry => entry.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] values =
            [
                entry.Id.ToString("N"),
                entry.RecordedAt.ToString("O", CultureInfo.InvariantCulture),
                entry.RecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                entry.RecordedAt.Offset.TotalMinutes.ToString(CultureInfo.InvariantCulture),
                entry.TimeZoneId,
                "Quick",
                ((int)entry.Mood).ToString(CultureInfo.InvariantCulture),
                entry.Mood.ToString(),
                entry.Stress?.ToString(CultureInfo.InvariantCulture) ?? "",
                entry.Energy?.ToString(CultureInfo.InvariantCulture) ?? "",
                string.Join(";", entry.Factors),
                "1"
            ];
            await writer.WriteLineAsync(
                string.Join(",", values.Select((value, index) =>
                    Escape(value, neutralizeFormula: index == 4))).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value, bool neutralizeFormula)
    {
        var trimmed = value.AsSpan().TrimStart();
        // A quoted CSV cell can still be interpreted as a formula by spreadsheet applications.
        if (neutralizeFormula && ((!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@')
            || (value.Length > 0 && value[0] is '\t' or '\r' or '\n')))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
