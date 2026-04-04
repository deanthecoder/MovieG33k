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

        await m_libraryRepository.InitializeAsync(cancellationToken);

        var remoteTitles =
            string.IsNullOrWhiteSpace(query.Query)
                ? await m_tmdbMetadataClient.GetTrendingAsync(query.Kind, query.MaxResults, cancellationToken)
                : await m_tmdbMetadataClient.SearchAsync(query, cancellationToken);

        if (remoteTitles.Count > 0)
            await m_libraryRepository.UpsertTitlesAsync(remoteTitles, cancellationToken);

        var localMatches = await m_libraryRepository.SearchLibraryAsync(query.Query, query.Kind, query.MaxResults, cancellationToken);
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
        await m_libraryRepository.UpsertRatingAsync(
            new UserRating(title.Identifiers, title.Kind, stars * 2, updatedUtc),
            cancellationToken);
        await m_libraryRepository.UpsertWatchStateAsync(
            new WatchState(title.Identifiers, title.Kind, WatchStatus.Watched, updatedUtc),
            cancellationToken);
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
            appliedCount++;
        }

        return appliedCount;
    }

    /// <summary>
    /// Clears the local library database.
    /// </summary>
    public Task ResetLibraryAsync(CancellationToken cancellationToken = default) =>
        m_libraryRepository.ResetAsync(cancellationToken);

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

    private string BuildStatusText(DiscoveryQuery query, int localMatchCount, int remoteCount)
    {
        var mediaType = query.Kind == TitleKind.Movie ? "movies" : "TV shows";

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
