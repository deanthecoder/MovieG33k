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
    private const int CandidatePoolSize = 120;
    private const int AgeRatingEnrichmentLimit = 80;
    private const int AgeRatingEnrichmentParallelism = 6;
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

        var candidateQuery = query with
        {
            MaxResults = Math.Max(
                query.MaxResults * (HasDiscoveryFilters(query) ? 4 : 2),
                CandidatePoolSize)
        };
        var remoteCandidates = await m_tmdbMetadataClient.GetDiscoverAsync(candidateQuery, cancellationToken);
        if (remoteCandidates.Count == 0)
            return Array.Empty<RecommendationCandidate>();

        await m_libraryRepository.UpsertTitlesAsync(remoteCandidates, cancellationToken);

        var snapshotsByKey = await m_libraryRepository.GetByCatalogKeysAsync(
            remoteCandidates.Select(title => CatalogTitleKey.Create(title.Kind, title.Identifiers)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cancellationToken);

        IReadOnlyList<LibraryItemSnapshot> snapshots = remoteCandidates
            .Select(title =>
            {
                var key = CatalogTitleKey.Create(title.Kind, title.Identifiers);
                return snapshotsByKey.TryGetValue(key, out var snapshot)
                    ? snapshot
                    : new LibraryItemSnapshot(title);
            })
            .ToArray();

        if (ShouldEnrichAgeRatings(query))
            snapshots = await EnrichMissingMovieAgeRatingsAsync(snapshots, cancellationToken);

        var filteredResults = snapshots
            .Where(snapshot => !HasUserAlreadyHandled(snapshot))
            .Where(snapshot => MatchesQuery(snapshot.Title, query.Query))
            .Where(snapshot => MatchesGenreFilter(snapshot.Title, query.GenreFilter))
            .Where(snapshot => MatchesAgeRatingFilter(snapshot.Title, query.AgeRatingFilter))
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

    private static bool MatchesGenreFilter(CatalogTitle title, string genreFilter) =>
        string.IsNullOrWhiteSpace(genreFilter) ||
        title.Genres?.Any(genre => string.Equals(genre, genreFilter, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool MatchesAgeRatingFilter(CatalogTitle title, string ageRatingFilter)
    {
        if (string.IsNullOrWhiteSpace(ageRatingFilter))
            return true;

        var minimumAgeRank = GetMinimumAgeRank(ageRatingFilter);
        var titleAgeRank = GetAgeRatingRank(title.AgeRating);
        return titleAgeRank.HasValue && titleAgeRank.Value >= minimumAgeRank;
    }

    private static bool HasDiscoveryFilters(DiscoveryQuery query) =>
        !string.IsNullOrWhiteSpace(query.GenreFilter) ||
        !string.IsNullOrWhiteSpace(query.AgeRatingFilter);

    private static bool ShouldEnrichAgeRatings(DiscoveryQuery query) =>
        query.Kind == TitleKind.Movie &&
        !string.IsNullOrWhiteSpace(query.AgeRatingFilter);

    private async Task<IReadOnlyList<LibraryItemSnapshot>> EnrichMissingMovieAgeRatingsAsync(
        IReadOnlyList<LibraryItemSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var snapshotsToEnrich = snapshots
            .Where(snapshot => string.IsNullOrWhiteSpace(snapshot.Title.AgeRating))
            .Take(AgeRatingEnrichmentLimit)
            .ToArray();
        if (snapshotsToEnrich.Length == 0)
            return snapshots;

        var semaphore = new SemaphoreSlim(AgeRatingEnrichmentParallelism);
        var enrichedTitles = new Dictionary<string, CatalogTitle>(StringComparer.OrdinalIgnoreCase);

        await Task.WhenAll(snapshotsToEnrich.Select(async snapshot =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var detailedTitle = await m_tmdbMetadataClient.GetTitleDetailsAsync(snapshot.Title.Identifiers, snapshot.Title.Kind, cancellationToken);
                if (detailedTitle == null)
                    return;

                lock (enrichedTitles)
                {
                    enrichedTitles[CatalogTitleKey.Create(detailedTitle.Kind, detailedTitle.Identifiers)] = detailedTitle;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }));

        if (enrichedTitles.Count == 0)
            return snapshots;

        await m_libraryRepository.UpsertTitlesAsync(enrichedTitles.Values.ToArray(), cancellationToken);

        return snapshots
            .Select(snapshot =>
            {
                var catalogKey = CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers);
                return enrichedTitles.TryGetValue(catalogKey, out var detailedTitle)
                    ? snapshot with { Title = detailedTitle }
                    : snapshot;
            })
            .ToArray();
    }

    private static int GetMinimumAgeRank(string ageRatingFilter) =>
        ageRatingFilter switch
        {
            "12+" => 2,
            "15+" => 3,
            "18+" => 4,
            _ => 0
        };

    private static int? GetAgeRatingRank(string ageRating)
    {
        if (string.IsNullOrWhiteSpace(ageRating))
            return null;

        var normalized = ageRating.Trim().ToUpperInvariant();
        return normalized switch
        {
            "U" or "G" or "TV-G" or "TV-Y" or "TV-Y7" => 0,
            "PG" or "TV-PG" => 1,
            "12" or "12A" or "PG-13" or "TV-14" => 2,
            "15" or "R" or "MA15+" or "TV-MA" => 3,
            "18" or "NC-17" => 4,
            _ when int.TryParse(new string(normalized.TakeWhile(char.IsDigit).ToArray()), out var numericAge) => numericAge switch
            {
                >= 18 => 4,
                >= 15 => 3,
                >= 12 => 2,
                >= 0 => 1,
                _ => 1
            },
            _ => null
        };
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
