// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MovieG33k.Core.Models;

/// <summary>
/// Represents a single title row parsed from an IMDb export.
/// </summary>
/// <remarks>
/// IMDb import is treated as bootstrap data so the app can reconcile into its TMDb-first local model.
/// </remarks>
public sealed record ImportedImdbItem(
    string ImdbId,
    TitleKind Kind,
    string Title,
    int? Year,
    decimal? ImdbRating,
    int? UserRating,
    DateOnly? RatedOn,
    CatalogTitle ResolvedTitle = null);
