// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.IO;
using System.Linq;
using MovieG33k.Core.Models;

namespace MovieG33k.ViewModels;

/// <summary>
/// View-model wrapper for a discovered or cached library item.
/// </summary>
/// <remarks>
/// The shell can bind to presentation-friendly strings without pushing formatting logic back into the domain types.
/// </remarks>
public sealed class LibraryItemSnapshotViewModel
{
    /// <summary>
    /// Creates a new row view model.
    /// </summary>
    public LibraryItemSnapshotViewModel(LibraryItemSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Title = snapshot.Title.Name ?? "Untitled";
        Subtitle = BuildSubtitle(snapshot);
        SourceLabel = snapshot.SourceLabel ?? "TMDb";
        BadgeText = BuildBadgeText(snapshot);
        Overview = string.IsNullOrWhiteSpace(snapshot.Title.Overview)
            ? "Overview not fetched yet."
            : snapshot.Title.Overview;
        PersonalState = BuildPersonalState(snapshot);
        HasPersonalState = !string.IsNullOrWhiteSpace(PersonalState);
        ProviderText = snapshot.ProviderAvailabilities?.Count > 0
            ? string.Join(", ", snapshot.ProviderAvailabilities.Select(provider => provider.Provider.Name))
            : "Streaming availability will show up here when provider support is added.";
        PosterUrl = BuildPosterUrl(snapshot.Title.PosterPath);
    }

    /// <summary>
    /// Gets the underlying snapshot.
    /// </summary>
    public LibraryItemSnapshot Snapshot { get; }

    /// <summary>
    /// Gets the row title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the row subtitle.
    /// </summary>
    public string Subtitle { get; }

    /// <summary>
    /// Gets the row source label.
    /// </summary>
    public string SourceLabel { get; }

    /// <summary>
    /// Gets the compact badge text shown on the row.
    /// </summary>
    public string BadgeText { get; }

    /// <summary>
    /// Gets the detail overview text.
    /// </summary>
    public string Overview { get; }

    /// <summary>
    /// Gets the personal state summary.
    /// </summary>
    public string PersonalState { get; }

    /// <summary>
    /// Gets whether there is personal state worth showing in list rows.
    /// </summary>
    public bool HasPersonalState { get; }

    /// <summary>
    /// Gets the provider summary text.
    /// </summary>
    public string ProviderText { get; }

    /// <summary>
    /// Gets the full poster URL when available.
    /// </summary>
    public string PosterUrl { get; }

    private static string BuildSubtitle(LibraryItemSnapshot snapshot)
    {
        var mediaLabel = snapshot.Title.Kind == TitleKind.Movie ? "Movie" : "TV";
        var yearText = snapshot.Title.ReleaseYear?.ToString() ?? "TBA";
        var genreText = snapshot.Title.Genres?.Count > 0
            ? string.Join(" / ", snapshot.Title.Genres.Take(2))
            : null;

        return string.IsNullOrWhiteSpace(genreText)
            ? $"{mediaLabel} • {yearText}"
            : $"{mediaLabel} • {yearText} • {genreText}";
    }

    private static string BuildPersonalState(LibraryItemSnapshot snapshot)
    {
        var parts = new List<string>();

        if (snapshot.Rating != null)
            parts.Add($"Rated {GetStarRating(snapshot.Rating)}/5");

        if (snapshot.WatchState != null)
            parts.Add(snapshot.WatchState.Status switch
            {
                WatchStatus.NotStarted => "Not started",
                WatchStatus.InProgress => "In progress",
                WatchStatus.Watched => "Watched",
                WatchStatus.Abandoned => "Abandoned",
                _ => "Watch state unknown"
            });

        if (snapshot.WatchlistEntry != null)
            parts.Add("On watchlist");

        return parts.Count == 0
            ? string.Empty
            : string.Join(" • ", parts);
    }

    private static string BuildBadgeText(LibraryItemSnapshot snapshot)
    {
        if (snapshot.Rating != null)
            return $"{GetStarRating(snapshot.Rating)}/5";

        if (snapshot.WatchState?.Status == WatchStatus.Watched)
            return "Watched";

        if (snapshot.WatchlistEntry != null)
            return "Pinned";

        return snapshot.SourceLabel ?? "TMDb";
    }

    private static int GetStarRating(UserRating rating) => (int)Math.Round(rating.ScoreOutOfTen / 2.0, MidpointRounding.AwayFromZero);

    private static string BuildPosterUrl(string posterPath) =>
        string.IsNullOrWhiteSpace(posterPath)
            ? null
            : Path.IsPathRooted(posterPath)
                ? posterPath
            : Uri.TryCreate(posterPath, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri.ToString()
            : $"https://image.tmdb.org/t/p/w342{posterPath}";
}
