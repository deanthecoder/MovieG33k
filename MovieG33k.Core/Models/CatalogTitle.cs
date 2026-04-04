// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;

namespace MovieG33k.Core.Models;

/// <summary>
/// Base record for movie and TV metadata tracked by MovieG33k.
/// </summary>
/// <remarks>
/// This keeps shared catalog data in one place while still allowing movie- and TV-specific models to diverge later.
/// </remarks>
public abstract record CatalogTitle(
    TitleIdentifiers Identifiers,
    TitleKind Kind,
    string Name,
    string OriginalName,
    string Overview,
    DateOnly? ReleaseDate,
    string PosterPath,
    string BackdropPath,
    IReadOnlyList<string> Genres,
    string OriginalLanguage,
    decimal? PublicRating = null)
{
    /// <summary>
    /// Returns the release year when a release date is known.
    /// </summary>
    public int? ReleaseYear => ReleaseDate?.Year;
}
