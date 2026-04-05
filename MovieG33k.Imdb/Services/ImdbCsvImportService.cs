// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;

namespace MovieG33k.Imdb.Services;

/// <summary>
/// Imports IMDb CSV exports into MovieG33k's domain model.
/// </summary>
/// <remarks>
/// This stays intentionally modest for now: it parses the common exported columns and resolves IMDb IDs through the TMDb abstraction where possible.
/// </remarks>
public sealed class ImdbCsvImportService : IImdbImportService
{
    private const int MaxConcurrentResolutions = 8;
    private readonly ITmdbMetadataClient m_tmdbMetadataClient;

    /// <summary>
    /// Creates a new IMDb CSV import service.
    /// </summary>
    public ImdbCsvImportService(ITmdbMetadataClient tmdbMetadataClient)
    {
        m_tmdbMetadataClient = tmdbMetadataClient ?? throw new ArgumentNullException(nameof(tmdbMetadataClient));
    }

    /// <inheritdoc />
    public async Task<ImdbImportResult> ImportAsync(
        FileInfo csvFile,
        IProgress<ImdbImportProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (csvFile == null)
            throw new ArgumentNullException(nameof(csvFile));

        if (!csvFile.Exists)
            throw new FileNotFoundException("The IMDb CSV file could not be found.", csvFile.FullName);

        var lines = await File.ReadAllLinesAsync(csvFile.FullName, cancellationToken);
        if (lines.Length <= 1)
            return new ImdbImportResult(Array.Empty<ImportedImdbItem>(), 0, "The IMDb export file did not contain any title rows.");

        var headers = ParseCsvLine(lines[0]);
        var rows = lines
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => BuildImportRow(headers, ParseCsvLine(line)))
            .Where(row => !string.IsNullOrWhiteSpace(row.ImdbId))
            .ToArray();
        var totalRows = rows.Length;
        var items = new ImportedImdbItem[totalRows];
        var resolvedItemCount = 0;
        var progressCounter = new ProgressCounter();

        progress?.Report(new ImdbImportProgress(0, totalRows));

        using var concurrencyGate = new SemaphoreSlim(MaxConcurrentResolutions);
        var tasks = rows.Select((row, index) =>
            ImportRowAsync(row, index, items, concurrencyGate, progress, progressCounter, totalRows, cancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        foreach (var item in items)
            if (item?.ResolvedTitle != null)
                resolvedItemCount++;

        var statusText = resolvedItemCount == 0
            ? $"Imported {items.Length} IMDb row(s). No TMDb matches were resolved yet."
            : $"Imported {items.Length} IMDb row(s) and resolved {resolvedItemCount} item(s) to TMDb.";

        return new ImdbImportResult(items, resolvedItemCount, statusText);
    }

    private async Task ImportRowAsync(
        ImportRow row,
        int index,
        ImportedImdbItem[] items,
        SemaphoreSlim concurrencyGate,
        IProgress<ImdbImportProgress> progress,
        ProgressCounter progressCounter,
        int totalRows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await concurrencyGate.WaitAsync(cancellationToken);

        try
        {
            var resolvedTitle = await m_tmdbMetadataClient.ResolveImdbIdAsync(row.ImdbId, row.Kind, cancellationToken);
            items[index] = new ImportedImdbItem(
                row.ImdbId,
                row.Kind,
                row.Title,
                row.Year,
                row.ImdbRating,
                row.UserRating,
                row.RatedOn,
                resolvedTitle);
            var completedCount = Interlocked.Increment(ref progressCounter.Value);
            progress?.Report(new ImdbImportProgress(completedCount, totalRows, row.Title));
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    private static ImportRow BuildImportRow(IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        var row = BuildRow(headers, values);
        return new ImportRow(
            GetValue(row, "Const"),
            ParseTitleKind(GetValue(row, "Title Type")),
            GetValue(row, "Title"),
            TryParseInt(GetValue(row, "Year")),
            TryParseDecimal(GetValue(row, "IMDb Rating")),
            TryParseInt(GetValue(row, "Your Rating")),
            TryParseDateOnly(GetValue(row, "Date Rated")));
    }

    private static Dictionary<string, string> BuildRow(IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
            row[headers[i]] = i < values.Count ? values[i] : string.Empty;

        return row;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.Trim() : null;

    private static TitleKind ParseTitleKind(string titleType)
    {
        var normalizedTitleType = NormalizeTitleType(titleType);
        return normalizedTitleType.StartsWith("tv", StringComparison.OrdinalIgnoreCase)
            ? TitleKind.TvShow
            : TitleKind.Movie;
    }

    private static string NormalizeTitleType(string titleType) =>
        string.IsNullOrWhiteSpace(titleType)
            ? string.Empty
            : titleType
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);

    private static int? TryParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : null;

    private static decimal? TryParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : null;

    private static DateOnly? TryParseDateOnly(string value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedValue)
            ? parsedValue
            : null;

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new System.Text.StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                insideQuotes = !insideQuotes;
                continue;
            }

            if (current == ',' && !insideQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        values.Add(builder.ToString());
        return values;
    }

    private sealed record ImportRow(
        string ImdbId,
        TitleKind Kind,
        string Title,
        int? Year,
        decimal? ImdbRating,
        int? UserRating,
        DateOnly? RatedOn);

    private sealed class ProgressCounter
    {
        public int Value;
    }
}
