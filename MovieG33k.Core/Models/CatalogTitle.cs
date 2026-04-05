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
using System;

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
    decimal? PublicRating = null,
    string AgeRating = null,
    IReadOnlyList<string> Directors = null)
{
    public const string UnknownAgeRating = "[Unknown]";
    public const string UnknownDirector = "[Unknown]";
    public const string UnknownPosterPath = "[Unavailable]";
    public const int UnknownRuntimeMinutes = -1;

    /// <summary>
    /// Returns the release year when a release date is known.
    /// </summary>
    public int? ReleaseYear => ReleaseDate?.Year;

    /// <summary>
    /// Returns true when a user-visible age rating is known.
    /// </summary>
    public bool HasKnownAgeRating =>
        !string.IsNullOrWhiteSpace(AgeRating) &&
        !string.Equals(AgeRating, UnknownAgeRating, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the poster field has been resolved, even if no poster is available.
    /// </summary>
    public bool HasResolvedPosterPath =>
        !string.IsNullOrWhiteSpace(PosterPath);

    /// <summary>
    /// Returns true when director metadata has been resolved, even if no named director is available.
    /// </summary>
    public bool HasResolvedDirectors =>
        Directors?.Count > 0;

    public static bool IsUnknownDirector(string director) =>
        string.Equals(director?.Trim(), UnknownDirector, StringComparison.OrdinalIgnoreCase);

    public static bool IsUnavailablePosterPath(string posterPath) =>
        string.Equals(posterPath?.Trim(), UnknownPosterPath, StringComparison.OrdinalIgnoreCase);
}
