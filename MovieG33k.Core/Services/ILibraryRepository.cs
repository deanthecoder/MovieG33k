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
/// Describes the local persistence contract for MovieG33k user data and cached metadata.
/// </summary>
/// <remarks>
/// The UI and orchestration layers depend on this abstraction rather than on SQLite-specific details.
/// </remarks>
public interface ILibraryRepository
{
    /// <summary>
    /// Ensures the local store is ready for use.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates catalog titles discovered from external services.
    /// </summary>
    Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates the user's rating for a title.
    /// </summary>
    Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates the user's watch progress for a title.
    /// </summary>
    Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a watchlist entry for a title.
    /// </summary>
    Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a watchlist entry for a title.
    /// </summary>
    Task DeleteWatchlistEntryAsync(
        TitleIdentifiers identifiers,
        TitleKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves provider availability rows for a title.
    /// </summary>
    Task UpsertProviderAvailabilityAsync(
        TitleIdentifiers identifiers,
        TitleKind kind,
        IReadOnlyList<ProviderAvailability> providerAvailabilities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the local library cache.
    /// </summary>
    Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(
        string query,
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns snapshots for the supplied catalog keys so remote results can be enriched with local user state.
    /// </summary>
    Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(
        IReadOnlyList<string> catalogKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns watched titles ordered by rating and recency.
    /// </summary>
    Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(
        string query,
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns watchlist titles ordered by priority and recency.
    /// </summary>
    Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(
        string query,
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's rated titles for insight calculations.
    /// </summary>
    Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(
        TitleKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns recently rated titles that are still missing important cached metadata.
    /// </summary>
    Task<IReadOnlyList<LibraryItemSnapshot>> GetRatedTitlesMissingMetadataAsync(
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the local MovieG33k database so the app can start fresh.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
