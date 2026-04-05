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
/// Describes the TMDb-facing metadata and discovery capabilities needed by the app.
/// </summary>
/// <remarks>
/// Keeping this contract in the core project lets MovieG33k use TMDb data without coupling orchestration to the HTTP layer.
/// </remarks>
public interface ITmdbMetadataClient
{
    /// <summary>
    /// Indicates whether the client is configured for live TMDb requests.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Gets the primary region code used when provider and discovery data are requested.
    /// </summary>
    string RegionCode { get; }

    /// <summary>
    /// Searches TMDb for titles that match the query.
    /// </summary>
    Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a discovery-oriented list of popular or trending titles.
    /// </summary>
    Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a steadier discovery pool suitable for recommendations and future filters.
    /// </summary>
    Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads richer metadata for a known title when available.
    /// </summary>
    Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, string titleName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an IMDb identifier to a TMDb-backed title.
    /// </summary>
    Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default);
}
