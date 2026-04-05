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
    private const int MinimumAgeRatingEnrichmentLimit = 160;
    private const int AgeRatingEnrichmentMultiplier = 4;
    private const int MinimumDirectorEnrichmentLimit = 80;
    private const int DirectorEnrichmentMultiplier = 2;
    private const int AgeRatingEnrichmentParallelism = 6;
    private const double GenreAffinityMultiplier = 3.4d;
    private const double DirectorAffinityMultiplier = 4.6d;
    private const double DecadeAffinityMultiplier = 1.8d;
    private const double PublicRatingMultiplier = 1.2d;
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
        var directorAffinity = BuildDirectorAffinity(ratedTitles);
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

        var shouldEnrichAgeRatings = ShouldEnrichAgeRatings(query);
        var shouldEnrichDirectors = ShouldEnrichDirectors(directorAffinity);
        if (shouldEnrichAgeRatings || shouldEnrichDirectors)
        {
            var ageRatingLimit = shouldEnrichAgeRatings
                ? Math.Max(query.MaxResults * AgeRatingEnrichmentMultiplier, MinimumAgeRatingEnrichmentLimit)
                : 0;
            var directorLimit = shouldEnrichDirectors
                ? Math.Max(query.MaxResults * DirectorEnrichmentMultiplier, MinimumDirectorEnrichmentLimit)
                : 0;
            var enrichmentLimit = Math.Min(snapshots.Count, Math.Max(ageRatingLimit, directorLimit));
            snapshots = await EnrichRecommendationMetadataAsync(
                snapshots,
                enrichmentLimit,
                shouldEnrichAgeRatings,
                shouldEnrichDirectors,
                cancellationToken);
        }

        var filteredResults = snapshots
            .Where(snapshot => !HasUserAlreadyHandled(snapshot))
            .Where(snapshot => MatchesQuery(snapshot.Title, query.Query))
            .Where(snapshot => MatchesGenreFilter(snapshot.Title, query.GenreFilter))
            .Where(snapshot => MatchesAgeRatingFilter(snapshot.Title, query.AgeRatingFilter))
            .Select(snapshot => ScoreCandidate(snapshot, genreAffinity, directorAffinity, decadeAffinity))
            .Where(candidate => candidate != null)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Title.PublicRating ?? 0)
            .ThenBy(candidate => candidate.Title.Name)
            .Take(query.MaxResults)
            .ToArray();

        return filteredResults;
    }

    private static Dictionary<string, double> BuildGenreAffinity(IReadOnlyList<RatedTitleInsight> ratedTitles)
    {
        var aggregates = new Dictionary<string, PreferenceAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in ratedTitles)
        {
            var preference = GetPreferenceValue(title.ScoreOutOfTen);
            var weight = GetPreferenceWeight(preference);
            if (weight <= 0d)
                continue;

            foreach (var genre in (title.Genres?.Count > 0 ? title.Genres : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                aggregates[genre] = aggregates.TryGetValue(genre, out var aggregate)
                    ? aggregate.Add(preference, weight)
                    : new PreferenceAggregate(preference * weight, weight);
            }
        }

        return aggregates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, double> BuildDirectorAffinity(IReadOnlyList<RatedTitleInsight> ratedTitles)
    {
        var aggregates = new Dictionary<string, PreferenceAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in ratedTitles)
        {
            var preference = GetPreferenceValue(title.ScoreOutOfTen);
            var weight = GetPreferenceWeight(preference);
            if (weight <= 0d)
                continue;

            foreach (var director in (title.Directors?.Count > 0 ? title.Directors : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                aggregates[director] = aggregates.TryGetValue(director, out var aggregate)
                    ? aggregate.Add(preference, weight)
                    : new PreferenceAggregate(preference * weight, weight);
            }
        }

        return aggregates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<int, double> BuildDecadeAffinity(IReadOnlyList<RatedTitleInsight> ratedTitles)
    {
        var aggregates = new Dictionary<int, PreferenceAggregate>();

        foreach (var title in ratedTitles.Where(title => title.ReleaseYear.HasValue))
        {
            var preference = GetPreferenceValue(title.ScoreOutOfTen);
            var weight = GetPreferenceWeight(preference);
            if (weight <= 0d)
                continue;

            var decade = (title.ReleaseYear!.Value / 10) * 10;
            aggregates[decade] = aggregates.TryGetValue(decade, out var aggregate)
                ? aggregate.Add(preference, weight)
                : new PreferenceAggregate(preference * weight, weight);
        }

        return aggregates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Value);
    }

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

    private static bool ShouldEnrichDirectors(IReadOnlyDictionary<string, double> directorAffinity) =>
        directorAffinity.Any(pair => pair.Value > 0.12d);

    private async Task<IReadOnlyList<LibraryItemSnapshot>> EnrichRecommendationMetadataAsync(
        IReadOnlyList<LibraryItemSnapshot> snapshots,
        int enrichmentLimit,
        bool requireAgeRatings,
        bool requireDirectors,
        CancellationToken cancellationToken)
    {
        var snapshotsToEnrich = snapshots
            .Where(snapshot =>
                (requireAgeRatings && snapshot.Title.Kind == TitleKind.Movie && string.IsNullOrWhiteSpace(snapshot.Title.AgeRating)) ||
                (requireDirectors && (snapshot.Title.Directors == null || snapshot.Title.Directors.Count == 0)))
            .Take(enrichmentLimit)
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
                _ => 1
            },
            _ => null
        };
    }

    private static double GetPreferenceValue(int scoreOutOfTen) =>
        Math.Clamp((scoreOutOfTen - 5d) / 5d, -1d, 1d);

    private static double GetPreferenceWeight(double preference)
    {
        var intensity = Math.Abs(preference);
        return intensity < 0.05d
            ? 0d
            : 0.35d + Math.Pow(intensity, 1.6d) * 1.65d;
    }

    private static RecommendationCandidate ScoreCandidate(
        LibraryItemSnapshot snapshot,
        IReadOnlyDictionary<string, double> genreAffinity,
        IReadOnlyDictionary<string, double> directorAffinity,
        IReadOnlyDictionary<int, double> decadeAffinity)
    {
        var score = 0d;
        var signals = new List<string>();

        foreach (var genre in snapshot.Title.Genres?.Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
        {
            if (!genreAffinity.TryGetValue(genre, out var genreScore))
                continue;

            score += genreScore * GenreAffinityMultiplier;
            if (genreScore > 0.12d)
                signals.Add(genre);
        }

        foreach (var director in snapshot.Title.Directors?.Distinct(StringComparer.OrdinalIgnoreCase) ?? Array.Empty<string>())
        {
            if (!directorAffinity.TryGetValue(director, out var directorScore))
                continue;

            score += directorScore * DirectorAffinityMultiplier;
            if (directorScore > 0.14d)
                signals.Add(director);
        }

        if (snapshot.Title.ReleaseYear is int releaseYear)
        {
            var decade = (releaseYear / 10) * 10;
            if (decadeAffinity.TryGetValue(decade, out var decadeScore))
            {
                score += decadeScore * DecadeAffinityMultiplier;
                if (decadeScore > 0.1d)
                    signals.Add($"{decade}s");
            }
        }

        if (snapshot.Title.PublicRating is decimal publicRating)
        {
            score += (double)(publicRating / 10m) * PublicRatingMultiplier;
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

    private readonly record struct PreferenceAggregate(double WeightedPreferenceTotal, double WeightTotal)
    {
        public double Value => WeightTotal <= 0d ? 0d : WeightedPreferenceTotal / WeightTotal;

        public PreferenceAggregate Add(double preference, double weight) =>
            new(WeightedPreferenceTotal + preference * weight, WeightTotal + weight);
    }
}
