// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Linq;
using DTC.Core;
using MovieG33k.Core.Models;

namespace MovieG33k.Core.Services;

/// <summary>
/// Coordinates local library data with TMDb discovery results.
/// </summary>
/// <remarks>
/// This is the main non-UI orchestration point for the early app experience, keeping persistence and HTTP concerns outside the view models.
/// </remarks>
public sealed class DiscoveryWorkspaceService
{
    private const int BackgroundRefreshParallelism = 3;
    private readonly ILibraryRepository m_libraryRepository;
    private readonly ITmdbMetadataClient m_tmdbMetadataClient;

    /// <summary>
    /// Creates a new discovery workspace service.
    /// </summary>
    public DiscoveryWorkspaceService(ILibraryRepository libraryRepository, ITmdbMetadataClient tmdbMetadataClient)
    {
        m_libraryRepository = libraryRepository ?? throw new ArgumentNullException(nameof(libraryRepository));
        m_tmdbMetadataClient = tmdbMetadataClient ?? throw new ArgumentNullException(nameof(tmdbMetadataClient));
    }

    /// <summary>
    /// Loads discovery results for the supplied query.
    /// </summary>
    public async Task<DiscoveryResultSet> DiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        Logger.Instance.Info(
            $"Discovering {query.Kind} titles for query '{(string.IsNullOrWhiteSpace(query.Query) ? "<trending>" : query.Query)}' " +
            $"with director filter '{query.DirectorFilter ?? "<any>"}' (max {query.MaxResults}).");
        await m_libraryRepository.InitializeAsync(cancellationToken);

        var remoteTitles =
            !string.IsNullOrWhiteSpace(query.DirectorFilter)
                ? Array.Empty<CatalogTitle>()
                : string.IsNullOrWhiteSpace(query.Query)
                ? await m_tmdbMetadataClient.GetTrendingAsync(query.Kind, query.MaxResults, cancellationToken)
                : await m_tmdbMetadataClient.SearchAsync(query, cancellationToken);

        if (remoteTitles.Count > 0)
            await m_libraryRepository.UpsertTitlesAsync(remoteTitles, cancellationToken);

        var localMatches = await m_libraryRepository.SearchLibraryAsync(query.Query, query.Kind, query.MaxResults, query.DirectorFilter, cancellationToken);
        var remoteSnapshotsByKey =
            remoteTitles.Count == 0
                ? new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LibraryItemSnapshot>(
                    await m_libraryRepository.GetByCatalogKeysAsync(
                        remoteTitles.Select(title => CatalogTitleKey.Create(title.Kind, title.Identifiers)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        cancellationToken),
                    StringComparer.OrdinalIgnoreCase);
        var mergedItems = MergeResults(localMatches, remoteTitles, remoteSnapshotsByKey, query.Query, query.MaxResults);
        var statusText = BuildStatusText(query, localMatches.Count, remoteTitles.Count);
        Logger.Instance.Info(
            $"Discovery returned {mergedItems.Count} merged {query.Kind} results ({localMatches.Count} local, {remoteTitles.Count} TMDb).");

        return new DiscoveryResultSet(query, mergedItems, statusText);
    }

    /// <summary>
    /// Loads the watched-library view ordered by rating.
    /// </summary>
    public async Task<DiscoveryResultSet> GetWatchedAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        await m_libraryRepository.InitializeAsync(cancellationToken);
        var watchedItems = await m_libraryRepository.GetWatchedAsync(query.Query, query.Kind, query.MaxResults, cancellationToken);
        Logger.Instance.Info($"Loaded {watchedItems.Count} watched {query.Kind} titles.");
        var mediaType = query.Kind == TitleKind.Movie ? "movies" : "TV shows";
        var statusText =
            watchedItems.Count == 0
                ? string.IsNullOrWhiteSpace(query.Query)
                    ? $"No watched {mediaType} yet."
                    : $"No watched {mediaType} matched \"{query.Query}\"."
                : string.IsNullOrWhiteSpace(query.Query)
                    ? $"Your watched {mediaType}, ordered by rating."
                    : $"Watched {mediaType} matching \"{query.Query}\".";

        return new DiscoveryResultSet(query, watchedItems, statusText);
    }

    /// <summary>
    /// Loads the watchlist view ordered by priority and recency.
    /// </summary>
    public async Task<DiscoveryResultSet> GetWatchlistAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        await m_libraryRepository.InitializeAsync(cancellationToken);
        var watchlistItems = await m_libraryRepository.GetWatchlistAsync(query.Query, query.Kind, query.MaxResults, cancellationToken);
        Logger.Instance.Info($"Loaded {watchlistItems.Count} watchlist {query.Kind} titles.");
        var mediaType = query.Kind == TitleKind.Movie ? "movies" : "TV shows";
        var statusText =
            watchlistItems.Count == 0
                ? string.IsNullOrWhiteSpace(query.Query)
                    ? $"No {mediaType} pinned to watch yet."
                    : $"No pinned {mediaType} matched \"{query.Query}\"."
                : string.IsNullOrWhiteSpace(query.Query)
                    ? $"Your pinned {mediaType}, ready for later."
                    : $"Pinned {mediaType} matching \"{query.Query}\".";

        return new DiscoveryResultSet(query, watchlistItems, statusText);
    }

    /// <summary>
    /// Builds rating insights for the supplied media type.
    /// </summary>
    public async Task<LibraryInsights> GetInsightsAsync(TitleKind kind, CancellationToken cancellationToken = default)
    {
        await m_libraryRepository.InitializeAsync(cancellationToken);
        var ratedTitles = await m_libraryRepository.GetRatedTitleInsightsAsync(kind, cancellationToken);
        Logger.Instance.Info($"Loaded {ratedTitles.Count} rated {kind} titles for insights.");

        var averageRating =
            ratedTitles.Count == 0
                ? 0
                : ratedTitles.Average(title => title.ScoreOutOfTen) / 2d;

        var ratingDistribution = Enumerable
            .Range(1, 5)
            .Select(stars => new RatingDistributionBucket(
                stars,
                ratedTitles.Count(title => ToStarRating(title.ScoreOutOfTen) == stars)))
            .ToArray();

        var ratingByDecade = ratedTitles
            .Where(title => title.ReleaseYear.HasValue)
            .GroupBy(title => (title.ReleaseYear!.Value / 10) * 10)
            .Select(group => new DecadeRatingBucket(
                group.Key,
                group.Count(),
                group.Average(title => title.ScoreOutOfTen) / 2d))
            .OrderBy(bucket => bucket.DecadeStartYear)
            .ToArray();

        var ratingByGenre = ratedTitles
            .SelectMany(title =>
                (title.Genres?.Count > 0 ? title.Genres : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(genre => new { Genre = genre, title.ScoreOutOfTen }))
            .GroupBy(entry => entry.Genre, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GenreRatingBucket(
                group.Key,
                group.Count(),
                group.Average(entry => entry.ScoreOutOfTen) / 2d))
            .OrderByDescending(bucket => bucket.AverageRatingOutOfFive)
            .ThenByDescending(bucket => bucket.TitleCount)
            .ThenBy(bucket => bucket.Genre)
            .ToArray();

        return new LibraryInsights(
            kind,
            ratedTitles.Count,
            averageRating,
            ratingDistribution,
            ratingByDecade,
            ratingByGenre);
    }

    /// <summary>
    /// Saves a 0-5 star rating and marks the title as watched.
    /// </summary>
    public async Task SaveRatingAsync(CatalogTitle title, int stars, CancellationToken cancellationToken = default)
    {
        if (title == null)
            throw new ArgumentNullException(nameof(title));

        if (stars is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(stars), "Star ratings must be between 0 and 5.");

        await m_libraryRepository.InitializeAsync(cancellationToken);
        await m_libraryRepository.UpsertTitlesAsync([title], cancellationToken);

        var updatedUtc = DateTimeOffset.UtcNow;
        Logger.Instance.Info($"Saving {stars}/5 rating for '{title.Name}' ({title.Kind}).");
        await m_libraryRepository.UpsertRatingAsync(
            new UserRating(title.Identifiers, title.Kind, stars * 2, updatedUtc),
            cancellationToken);
        await m_libraryRepository.UpsertWatchStateAsync(
            new WatchState(title.Identifiers, title.Kind, WatchStatus.Watched, updatedUtc),
            cancellationToken);
        await m_libraryRepository.DeleteWatchlistEntryAsync(title.Identifiers, title.Kind, cancellationToken);
    }

    /// <summary>
    /// Adds or removes a title from the watchlist.
    /// </summary>
    public async Task SetWatchlistStateAsync(CatalogTitle title, bool isOnWatchlist, CancellationToken cancellationToken = default)
    {
        if (title == null)
            throw new ArgumentNullException(nameof(title));

        await m_libraryRepository.InitializeAsync(cancellationToken);
        await m_libraryRepository.UpsertTitlesAsync([title], cancellationToken);
        Logger.Instance.Info($"{(isOnWatchlist ? "Pinning" : "Unpinning")} '{title.Name}' ({title.Kind}).");

        if (isOnWatchlist)
        {
            await m_libraryRepository.UpsertWatchlistEntryAsync(
                new WatchlistEntry(title.Identifiers, title.Kind, DateTimeOffset.UtcNow, 1),
                cancellationToken);
            return;
        }

        await m_libraryRepository.DeleteWatchlistEntryAsync(title.Identifiers, title.Kind, cancellationToken);
    }

    /// <summary>
    /// Persists resolved IMDb import rows into the local library.
    /// </summary>
    public async Task<int> ApplyImportAsync(ImdbImportResult importResult, CancellationToken cancellationToken = default)
    {
        if (importResult == null)
            throw new ArgumentNullException(nameof(importResult));

        await m_libraryRepository.InitializeAsync(cancellationToken);
        Logger.Instance.Info($"Applying IMDb import with {importResult.Items.Count} resolved candidates.");

        var resolvedTitles = importResult.Items
            .Where(item => item.ResolvedTitle != null)
            .Select(item => item.ResolvedTitle)
            .Distinct()
            .ToArray();
        if (resolvedTitles.Length > 0)
            await m_libraryRepository.UpsertTitlesAsync(resolvedTitles, cancellationToken);

        var appliedCount = 0;
        foreach (var item in importResult.Items.Where(row => row.ResolvedTitle != null))
        {
            var watchedUtc =
                item.RatedOn.HasValue
                    ? new DateTimeOffset(item.RatedOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : DateTimeOffset.UtcNow;

            if (item.UserRating.HasValue)
            {
                await m_libraryRepository.UpsertRatingAsync(
                    new UserRating(
                        item.ResolvedTitle.Identifiers,
                        item.ResolvedTitle.Kind,
                        Math.Clamp(item.UserRating.Value, 0, 10),
                        watchedUtc),
                    cancellationToken);
            }

            await m_libraryRepository.UpsertWatchStateAsync(
                new WatchState(item.ResolvedTitle.Identifiers, item.ResolvedTitle.Kind, WatchStatus.Watched, watchedUtc),
                cancellationToken);
            await m_libraryRepository.DeleteWatchlistEntryAsync(item.ResolvedTitle.Identifiers, item.ResolvedTitle.Kind, cancellationToken);
            appliedCount++;
        }

        Logger.Instance.Info($"Applied {appliedCount} IMDb import items to the local library.");
        return appliedCount;
    }

    /// <summary>
    /// Loads richer metadata for a known title and merges it with local user state.
    /// </summary>
    public async Task<LibraryItemSnapshot> GetTitleDetailsAsync(CatalogTitle title, CancellationToken cancellationToken = default)
    {
        if (title == null)
            throw new ArgumentNullException(nameof(title));

        await m_libraryRepository.InitializeAsync(cancellationToken);
        Logger.Instance.Info($"Refreshing detailed metadata for '{title.Name}' ({title.Kind}).");
        var detailedTitle = await m_tmdbMetadataClient.GetTitleDetailsAsync(title.Identifiers, title.Kind, title.Name, cancellationToken) ?? title;
        await m_libraryRepository.UpsertTitlesAsync([detailedTitle], cancellationToken);
        var normalizedSnapshot = await NormalizeUnavailableMetadataAsync(title, detailedTitle, cancellationToken);

        var result = normalizedSnapshot ?? await GetSnapshotAfterRefreshAsync(title, detailedTitle, cancellationToken) ?? new LibraryItemSnapshot(detailedTitle);
        if (!IsMetadataComplete(result.Title))
            Logger.Instance.Warn($"Detailed metadata for '{result.Title.Name}' is still incomplete after refresh.");

        return result;
    }

    /// <summary>
    /// Returns the current startup-sized batch of cached titles that still need richer metadata.
    /// </summary>
    public async Task<int> GetPendingMetadataRefreshCountAsync(
        int maxResultsPerKind = 24,
        CancellationToken cancellationToken = default)
    {
        if (!m_tmdbMetadataClient.IsConfigured)
            return 0;

        await m_libraryRepository.InitializeAsync(cancellationToken);

        var movieSnapshots = await m_libraryRepository.GetTitlesMissingMetadataAsync(TitleKind.Movie, maxResultsPerKind, cancellationToken);
        var tvSnapshots = await m_libraryRepository.GetTitlesMissingMetadataAsync(TitleKind.TvShow, maxResultsPerKind, cancellationToken);
        return movieSnapshots
            .Concat(tvSnapshots)
            .Select(snapshot => CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>
    /// Background-refreshes cached titles that are still missing key metadata.
    /// </summary>
    public async Task<int> RefreshMissingMetadataForRatedTitlesAsync(
        int maxResultsPerKind = 24,
        IProgress<MetadataRefreshProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!m_tmdbMetadataClient.IsConfigured)
        {
            Logger.Instance.Info("Skipping background rated-title metadata refresh because TMDb is not configured.");
            return 0;
        }

        await m_libraryRepository.InitializeAsync(cancellationToken);

        var movieSnapshots = await m_libraryRepository.GetTitlesMissingMetadataAsync(TitleKind.Movie, maxResultsPerKind, cancellationToken);
        var tvSnapshots = await m_libraryRepository.GetTitlesMissingMetadataAsync(TitleKind.TvShow, maxResultsPerKind, cancellationToken);
        var pendingSnapshots = movieSnapshots
            .Concat(tvSnapshots)
            .GroupBy(snapshot => CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (pendingSnapshots.Length == 0)
        {
            Logger.Instance.Info("Background metadata refresh found nothing to update.");
            progress?.Report(new MetadataRefreshProgress(0, 0, 0, null));
            return 0;
        }

        Logger.Instance.Info($"Starting background refresh for {pendingSnapshots.Length} cached titles with missing metadata.");
        progress?.Report(new MetadataRefreshProgress(0, pendingSnapshots.Length, 0, null));

        var semaphore = new SemaphoreSlim(BackgroundRefreshParallelism);
        var refreshedCount = 0;
        var processedCount = 0;

        await Task.WhenAll(pendingSnapshots.Select(async snapshot =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var detailedTitle = await m_tmdbMetadataClient.GetTitleDetailsAsync(snapshot.Title.Identifiers, snapshot.Title.Kind, snapshot.Title.Name, cancellationToken);
                if (detailedTitle == null)
                    return;

                await m_libraryRepository.UpsertTitlesAsync([detailedTitle], cancellationToken);
                var refreshedSnapshot =
                    await NormalizeUnavailableMetadataAsync(snapshot.Title, detailedTitle, cancellationToken) ??
                    await GetSnapshotAfterRefreshAsync(snapshot.Title, detailedTitle, cancellationToken);
                if (refreshedSnapshot != null && IsMetadataComplete(refreshedSnapshot.Title))
                {
                    Interlocked.Increment(ref refreshedCount);
                }
                else
                {
                    Logger.Instance.Warn($"Metadata for '{snapshot.Title.Name}' is still incomplete after refresh.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Instance.Exception($"Background metadata refresh failed for '{snapshot.Title.Name}'.", ex);
            }
            finally
            {
                var processed = Interlocked.Increment(ref processedCount);
                progress?.Report(new MetadataRefreshProgress(processed, pendingSnapshots.Length, refreshedCount, snapshot.Title.Name));
                semaphore.Release();
            }
        }));

        Logger.Instance.Info($"Background metadata refresh completed. Refreshed {refreshedCount} titles.");
        return refreshedCount;
    }

    /// <summary>
    /// Clears the local library database.
    /// </summary>
    public Task ResetLibraryAsync(CancellationToken cancellationToken = default)
    {
        Logger.Instance.Warn("Resetting the local MovieG33k library.");
        return m_libraryRepository.ResetAsync(cancellationToken);
    }

    private static IReadOnlyList<LibraryItemSnapshot> MergeResults(
        IReadOnlyList<LibraryItemSnapshot> localMatches,
        IReadOnlyList<CatalogTitle> remoteTitles,
        IReadOnlyDictionary<string, LibraryItemSnapshot> remoteSnapshotsByKey,
        string queryText,
        int maxResults)
    {
        var results = new List<LibraryItemSnapshot>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remoteSourceLabel = string.IsNullOrWhiteSpace(queryText) ? "Popular now" : "Search hit";

        foreach (var localMatch in localMatches)
        {
            var key = CatalogTitleKey.Create(localMatch.Title.Kind, localMatch.Title.Identifiers);
            if (!seenKeys.Add(key))
                continue;

            results.Add(localMatch with
            {
                SourceLabel =
                    string.IsNullOrWhiteSpace(localMatch.SourceLabel) ||
                    string.Equals(localMatch.SourceLabel, "Local", StringComparison.OrdinalIgnoreCase)
                        ? HasPersonalData(localMatch)
                            ? "In your library"
                            : remoteSourceLabel
                        : localMatch.SourceLabel
            });
            if (results.Count >= maxResults)
                return results;
        }

        foreach (var remoteTitle in remoteTitles)
        {
            var key = CatalogTitleKey.Create(remoteTitle.Kind, remoteTitle.Identifiers);
            if (!seenKeys.Add(key))
                continue;

            if (remoteSnapshotsByKey.TryGetValue(key, out var snapshot))
            {
                results.Add(snapshot with
                {
                    SourceLabel = HasPersonalData(snapshot) ? "In your library" : remoteSourceLabel
                });
            }
            else
            {
                results.Add(new LibraryItemSnapshot(remoteTitle, SourceLabel: remoteSourceLabel));
            }
            if (results.Count >= maxResults)
                return results;
        }

        if (string.IsNullOrWhiteSpace(queryText))
            return results;

        return results
            .Select((item, index) => new { Item = item, Index = index, Score = GetMatchScore(item.Title, queryText) })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Index)
            .Select(result => result.Item)
            .Take(maxResults)
            .ToArray();
    }

    private static int GetMatchScore(CatalogTitle title, string queryText)
    {
        var query = queryText?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return 0;

        return Math.Max(GetTitleMatchScore(title.Name, query), GetTitleMatchScore(title.OriginalName, query));
    }

    private static int GetTitleMatchScore(string title, string query)
    {
        if (string.IsNullOrWhiteSpace(title))
            return 0;

        if (string.Equals(title, query, StringComparison.OrdinalIgnoreCase))
            return 1000;

        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 700;

        return title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 350 : 0;
    }

    private static bool HasPersonalData(LibraryItemSnapshot snapshot) =>
        snapshot.Rating != null ||
        snapshot.WatchState != null ||
        snapshot.WatchlistEntry != null;

    private static int ToStarRating(int scoreOutOfTen) =>
        (int)Math.Round(scoreOutOfTen / 2d, MidpointRounding.AwayFromZero);

    private async Task<LibraryItemSnapshot> GetSnapshotAfterRefreshAsync(
        CatalogTitle originalTitle,
        CatalogTitle refreshedTitle,
        CancellationToken cancellationToken)
    {
        var candidateKeys = new[]
        {
            CatalogTitleKey.Create(refreshedTitle.Kind, refreshedTitle.Identifiers),
            CatalogTitleKey.Create(originalTitle.Kind, originalTitle.Identifiers)
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
        var snapshotsByKey = await m_libraryRepository.GetByCatalogKeysAsync(candidateKeys, cancellationToken);
        foreach (var key in candidateKeys)
        {
            if (snapshotsByKey.TryGetValue(key, out var snapshot))
                return snapshot;
        }

        return null;
    }

    private async Task<LibraryItemSnapshot> NormalizeUnavailableMetadataAsync(
        CatalogTitle originalTitle,
        CatalogTitle refreshedTitle,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAfterRefreshAsync(originalTitle, refreshedTitle, cancellationToken);
        if (snapshot == null || IsMetadataComplete(snapshot.Title))
            return snapshot;

        var normalizedTitle = FillUnavailableMetadataPlaceholders(snapshot.Title);
        if (ReferenceEquals(normalizedTitle, snapshot.Title))
            return snapshot;

        await m_libraryRepository.UpsertTitlesAsync([normalizedTitle], cancellationToken);
        return await GetSnapshotAfterRefreshAsync(originalTitle, normalizedTitle, cancellationToken)
               ?? new LibraryItemSnapshot(normalizedTitle, snapshot.Rating, snapshot.WatchState, snapshot.WatchlistEntry, snapshot.ProviderAvailabilities, snapshot.SourceLabel);
    }

    private static CatalogTitle FillUnavailableMetadataPlaceholders(CatalogTitle title) =>
        title switch
        {
            MovieEntry movie => movie with
            {
                PosterPath = title.HasResolvedPosterPath ? title.PosterPath : CatalogTitle.UnknownPosterPath,
                AgeRating = string.IsNullOrWhiteSpace(movie.AgeRating) && RequiresReleasedMovieMetadata(title) ? CatalogTitle.UnknownAgeRating : movie.AgeRating,
                Directors = movie.HasResolvedDirectors ? movie.Directors : [CatalogTitle.UnknownDirector],
                RuntimeMinutes = movie.RuntimeMinutes
            },
            TvShowEntry tvShow => tvShow with
            {
                PosterPath = title.HasResolvedPosterPath ? title.PosterPath : CatalogTitle.UnknownPosterPath
            },
            _ => title
        };

    private static bool IsMetadataComplete(CatalogTitle title) =>
        title != null &&
        title.HasResolvedPosterPath &&
        (title.Kind != TitleKind.Movie || !RequiresReleasedMovieMetadata(title) || !string.IsNullOrWhiteSpace(title.AgeRating)) &&
        (title.Kind != TitleKind.Movie || title.HasResolvedDirectors);

    private static bool RequiresReleasedMovieMetadata(CatalogTitle title) =>
        title.Kind == TitleKind.Movie &&
        (!title.ReleaseDate.HasValue || title.ReleaseDate.Value <= DateOnly.FromDateTime(DateTime.UtcNow));

    private string BuildStatusText(DiscoveryQuery query, int localMatchCount, int remoteCount)
    {
        var mediaType = query.Kind == TitleKind.Movie ? "movies" : "TV shows";

        if (!string.IsNullOrWhiteSpace(query.DirectorFilter))
        {
            return localMatchCount == 0
                ? string.IsNullOrWhiteSpace(query.Query)
                    ? $"No {mediaType} directed by {query.DirectorFilter} are in your library yet."
                    : $"No {mediaType} directed by {query.DirectorFilter} matched \"{query.Query}\"."
                : string.IsNullOrWhiteSpace(query.Query)
                    ? $"Showing {mediaType} in your library directed by {query.DirectorFilter}."
                    : $"Showing {mediaType} directed by {query.DirectorFilter} matching \"{query.Query}\".";
        }

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return $"Popular {mediaType} to browse right now.";
        }

        if (localMatchCount == 0 && remoteCount == 0)
            return $"No {mediaType} matched \"{query.Query}\".";

        if (localMatchCount > 0 && remoteCount > 0 || remoteCount > 0)
            return $"Showing {mediaType} that match \"{query.Query}\".";

        return $"Showing titles from your library that match \"{query.Query}\".";
    }
}
