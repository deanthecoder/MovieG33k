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

namespace MovieG33k.Core.Models;

/// <summary>
/// Builds stable local keys for titles regardless of the source identifier used.
/// </summary>
/// <remarks>
/// SQLite storage needs a single consistent key even when only TMDb or IMDb metadata is currently available.
/// </remarks>
public static class CatalogTitleKey
{
    /// <summary>
    /// Creates a stable key for the supplied identifiers.
    /// </summary>
    public static string Create(TitleKind kind, TitleIdentifiers identifiers)
    {
        if (identifiers == null)
            throw new ArgumentNullException(nameof(identifiers));

        if (identifiers.TmdbId.HasValue)
            return $"{kind}:tmdb:{identifiers.TmdbId.Value}";

        if (!string.IsNullOrWhiteSpace(identifiers.ImdbId))
            return $"{kind}:imdb:{identifiers.ImdbId.Trim()}";

        throw new InvalidOperationException("Titles must provide either a TMDb or IMDb identifier.");
    }
}
