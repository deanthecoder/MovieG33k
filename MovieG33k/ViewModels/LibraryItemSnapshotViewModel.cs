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
using DTC.Core.ViewModels;
using MovieG33k.Core.Models;

namespace MovieG33k.ViewModels;

/// <summary>
/// View-model wrapper for a discovered or cached library item.
/// </summary>
/// <remarks>
/// The shell can bind to presentation-friendly strings without pushing formatting logic back into the domain types.
/// </remarks>
public sealed class LibraryItemSnapshotViewModel : ViewModelBase
{
    private LibraryItemSnapshot m_snapshot;
    private string m_title;
    private string m_subtitle;
    private string m_sourceLabel;
    private string m_badgeText;
    private string m_overview;
    private string m_personalState;
    private bool m_hasPersonalState;
    private string m_providerText;
    private string m_posterUrl;

    /// <summary>
    /// Creates a new row view model.
    /// </summary>
    public LibraryItemSnapshotViewModel(LibraryItemSnapshot snapshot)
    {
        UpdateFromSnapshot(snapshot);
    }

    /// <summary>
    /// Gets the underlying snapshot.
    /// </summary>
    public LibraryItemSnapshot Snapshot
    {
        get => m_snapshot;
        private set => SetField(ref m_snapshot, value);
    }

    /// <summary>
    /// Gets the row title.
    /// </summary>
    public string Title
    {
        get => m_title;
        private set => SetField(ref m_title, value);
    }

    /// <summary>
    /// Gets the row subtitle.
    /// </summary>
    public string Subtitle
    {
        get => m_subtitle;
        private set => SetField(ref m_subtitle, value);
    }

    /// <summary>
    /// Gets the row source label.
    /// </summary>
    public string SourceLabel
    {
        get => m_sourceLabel;
        private set => SetField(ref m_sourceLabel, value);
    }

    /// <summary>
    /// Gets the compact badge text shown on the row.
    /// </summary>
    public string BadgeText
    {
        get => m_badgeText;
        private set => SetField(ref m_badgeText, value);
    }

    /// <summary>
    /// Gets the detail overview text.
    /// </summary>
    public string Overview
    {
        get => m_overview;
        private set => SetField(ref m_overview, value);
    }

    /// <summary>
    /// Gets the personal state summary.
    /// </summary>
    public string PersonalState
    {
        get => m_personalState;
        private set => SetField(ref m_personalState, value);
    }

    /// <summary>
    /// Gets whether there is personal state worth showing in list rows.
    /// </summary>
    public bool HasPersonalState
    {
        get => m_hasPersonalState;
        private set => SetField(ref m_hasPersonalState, value);
    }

    /// <summary>
    /// Gets the provider summary text.
    /// </summary>
    public string ProviderText
    {
        get => m_providerText;
        private set => SetField(ref m_providerText, value);
    }

    /// <summary>
    /// Gets the full poster URL when available.
    /// </summary>
    public string PosterUrl
    {
        get => m_posterUrl;
        private set => SetField(ref m_posterUrl, value);
    }

    /// <summary>
    /// Updates the row in place so list selection and keyboard focus can stay stable.
    /// </summary>
    public void UpdateFromSnapshot(LibraryItemSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        Snapshot = snapshot;
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
            : Path.IsPathRooted(posterPath) && File.Exists(posterPath)
                ? posterPath
            : Uri.TryCreate(posterPath, UriKind.Absolute, out var absoluteUri) && !absoluteUri.IsFile
                ? absoluteUri.ToString()
            : $"https://image.tmdb.org/t/p/w342{posterPath}";
}
