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
/// Represents the local view of a title with attached user state.
/// </summary>
/// <remarks>
/// This gives the UI and recommendation logic one combined shape without forcing the database to denormalize permanently.
/// </remarks>
public sealed record LibraryItemSnapshot(
    CatalogTitle Title,
    UserRating Rating = null,
    WatchState WatchState = null,
    WatchlistEntry WatchlistEntry = null,
    IReadOnlyList<ProviderAvailability> ProviderAvailabilities = null,
    string SourceLabel = null);
