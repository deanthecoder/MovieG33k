// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MovieG33k.Core.Models;

namespace MovieG33k.Core.Services;

/// <summary>
/// Builds a lightweight recommendation list from the user's rated history.
/// </summary>
public sealed class LocalTasteRecommendationService : IRecommendationService
{
    private const int CandidatePoolSize = 80;
    private readonly ILibraryRepository m_libraryRepository;
    private readonly ITmdbMetadataClient m_tmdbMetadataClient;

    /// <summary>
    /// Creates a new recommendation service.
    /// </summary>
    public LocalTasteRecommendationService(ILibraryRepository libraryRepository, ITmdbMetadataClient tmdbMetadataClient)
    {
        m_libraryRepository = libraryRepository ?? throw new ArgumentNullException(nameof(libraryRepository));
        m_tmdbMetadataClient = tmdbMetadataClient ?? throw new ArgumentNullException(nameof(tmdbMetadataClient));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecommendationCandidate>> GetRecommendationsAsync(
        DiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        await m_libraryRepository.InitializeAsync(cancellationToken);
        var ratedTitles = await m_libraryRepository.GetRatedTitleInsightsAsync(query.Kind, cancellationToken);
        if (ratedTitles.Count == 0)
            return Array.Empty<RecommendationCandidate>();

        var genreAffinity = BuildGenreAffinity(ratedTitles);
        var decadeAffinity = BuildDecadeAffinity(ratedTitles);

        var remoteCandidates = await m_tmdbMetadataClient.GetTrendingAsync(query.Kind, Math.Max(query.MaxResults * 3, CandidatePoolSize), cancellationToken);
        if (remoteCandidates.Count == 0)
            return Array.Empty<RecommendationCandidate>();

        await m_libraryRepository.UpsertTitlesAsync(remoteCandidates, cancellationToken);

        var snapshotsByKey = await m_libraryRepository.GetByCatalogKeysAsync(
            remoteCandidates.Select(title => CatalogTitleKey.Create(title.Kind, title.Identifiers)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cancellationToken);

        var filteredResults = remoteCandidates
            .Select(title =>
            {
                var key = CatalogTitleKey.Create(title.Kind, title.Identifiers);
                return snapshotsByKey.TryGetValue(key, out var snapshot)
                    ? snapshot
                    : new LibraryItemSnapshot(title);
            })
            .Where(snapshot => !HasUserAlreadyHandled(snapshot))
            .Where(snapshot => MatchesQuery(snapshot.Title, query.Query))
            .Select(snapshot => ScoreCandidate(snapshot, genreAffinity, decadeAffinity))
            .Where(candidate => candidate != null)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Title.PublicRating ?? 0)
            .ThenBy(candidate => candidate.Title.Name)
            .Take(query.MaxResults)
            .ToArray();

        return filteredResults;
    }

    private static Dictionary<string, double> BuildGenreAffinity(IReadOnlyList<RatedTitleInsight> ratedTitles) =>
        ratedTitles
            .SelectMany(title =>
                (title.Genres?.Count > 0 ? title.Genres : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(genre => new { Genre = genre, Score = title.ScoreOutOfTen / 2d }))
            .GroupBy(entry => entry.Genre, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Average(entry => entry.Score),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, double> BuildDecadeAffinity(IReadOnlyList<RatedTitleInsight> ratedTitles) =>
        ratedTitles
            .Where(title => title.ReleaseYear.HasValue)
            .GroupBy(title => (title.ReleaseYear!.Value / 10) * 10)
            .ToDictionary(
                group => group.Key,
                group => group.Average(title => title.ScoreOutOfTen / 2d));

    private static bool HasUserAlreadyHandled(LibraryItemSnapshot snapshot) =>
        snapshot.Rating != null ||
        snapshot.WatchlistEntry != null ||
        snapshot.WatchState?.Status == WatchStatus.Watched;

    private static bool MatchesQuery(CatalogTitle title, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var trimmedQuery = query.Trim();
        return title.Name?.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) == true ||
               title.OriginalName?.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) == true ||
               title.Genres?.Any(genre => genre.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static RecommendationCandidate ScoreCandidate(
        LibraryItemSnapshot snapshot,
        IReadOnlyDictionary<string, double> genreAffinity,
        IReadOnlyDictionary<int, double> decadeAffinity)
    {
        var score = 0d;
        var signals = new List<string>();

        foreach (var genre in snapshot.Title.Genres?.Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
        {
            if (!genreAffinity.TryGetValue(genre, out var genreScore))
                continue;

            score += genreScore * 1.2d;
            signals.Add(genre);
        }

        if (snapshot.Title.ReleaseYear is int releaseYear)
        {
            var decade = (releaseYear / 10) * 10;
            if (decadeAffinity.TryGetValue(decade, out var decadeScore))
            {
                score += decadeScore * 0.8d;
                signals.Add($"{decade}s");
            }
        }

        if (snapshot.Title.PublicRating is decimal publicRating)
        {
            score += (double)(publicRating / 4m);
            if (publicRating >= 7.5m)
                signals.Add("Highly rated");
        }

        if (score <= 0)
            return null;

        var distinctSignals = signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return new RecommendationCandidate(
            snapshot.Title,
            score,
            distinctSignals.Length == 0 ? "Recommended" : string.Join(" • ", distinctSignals),
            distinctSignals);
    }
}
