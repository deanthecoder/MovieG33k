// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DTC.Core;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.UI;
using DTC.Core.ViewModels;
using Material.Icons;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;

namespace MovieG33k.ViewModels;

/// <summary>
/// Drives the main MovieG33k desktop experience.
/// </summary>
/// <remarks>
/// The shell is intentionally centered on browsing, saving a watchlist, and rating what you watched.
/// </remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
    private const int InitialResultLimit = 100;
    private const int ResultLimitIncrement = 50;

    private enum LibraryViewMode
    {
        Discover,
        Watchlist,
        Watched
    }

    private static readonly HttpClient PosterHttpClient = CreatePosterHttpClient();
    private readonly DiscoveryWorkspaceService m_discoveryWorkspaceService;
    private readonly IImdbImportService m_imdbImportService;
    private readonly IDialogService m_dialogService;
    private readonly AsyncRelayCommand m_loadMoreResultsCommand;
    private readonly RelayCommand m_openSelectedExternalLinkCommand;
    private readonly string m_regionCode;
    private string m_searchText;
    private DiscoveryKindOption m_selectedKind;
    private LibraryItemSnapshotViewModel m_selectedResult;
    private string m_statusText = "Loading movies...";
    private bool m_isBusy;
    private bool m_canLoadMore;
    private LibraryViewMode m_currentMode;
    private int m_resultLimit = InitialResultLimit;
    private Bitmap m_selectedPoster;
    private CancellationTokenSource m_refreshCancellationTokenSource;
    private CancellationTokenSource m_posterCancellationTokenSource;
    private CancellationTokenSource m_selectedDetailsCancellationTokenSource;
    private readonly HashSet<string> m_enrichedDetailKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new main window view model.
    /// </summary>
    public MainWindowViewModel(
        DiscoveryWorkspaceService discoveryWorkspaceService,
        IImdbImportService imdbImportService,
        IDialogService dialogService,
        string regionCode = "GB")
    {
        m_discoveryWorkspaceService = discoveryWorkspaceService ?? throw new ArgumentNullException(nameof(discoveryWorkspaceService));
        m_imdbImportService = imdbImportService ?? throw new ArgumentNullException(nameof(imdbImportService));
        m_dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        m_regionCode = string.IsNullOrWhiteSpace(regionCode) ? "GB" : regionCode.Trim().ToUpperInvariant();

        KindOptions =
        [
            new DiscoveryKindOption("Movies", TitleKind.Movie),
            new DiscoveryKindOption("TV Shows", TitleKind.TvShow)
        ];
        m_selectedKind = KindOptions[0];
        m_currentMode = LibraryViewMode.Discover;
        Results = new ObservableCollection<LibraryItemSnapshotViewModel>();

        ShowDiscoverCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Discover);
        ShowWatchlistCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Watchlist);
        ShowWatchedCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Watched);
        ImportImdbRatingsCommand = new AsyncRelayCommand(
            _ => ImportImdbRatingsAsync(),
            onException: exception => m_dialogService.ShowMessage("Import failed", exception.Message));
        ResetDatabaseCommand = new AsyncRelayCommand(
            _ => ResetDatabaseAsync(),
            onException: exception => m_dialogService.ShowMessage("Couldn't reset database", exception.Message));
        RateSelectedTitleCommand = new AsyncRelayCommand(
            RateSelectedTitleAsync,
            onException: exception => m_dialogService.ShowMessage("Couldn't save rating", exception.Message));
        ToggleWatchlistCommand = new AsyncRelayCommand(
            _ => ToggleWatchlistAsync(),
            onException: exception => m_dialogService.ShowMessage("Couldn't update watchlist", exception.Message));
        m_openSelectedExternalLinkCommand = new RelayCommand(
            _ => OpenSelectedExternalLink(),
            _ => CanOpenSelectedExternalLink);
        m_loadMoreResultsCommand = new AsyncRelayCommand(
            _ => LoadMoreResultsAsync(),
            _ => CanLoadMore,
            onException: exception => m_dialogService.ShowMessage("Couldn't load more titles", exception.Message));

        _ = RefreshResultsAsync();
    }

    public IReadOnlyList<DiscoveryKindOption> KindOptions { get; }

    public ObservableCollection<LibraryItemSnapshotViewModel> Results { get; }

    public ICommand ShowDiscoverCommand { get; }

    public ICommand ShowWatchlistCommand { get; }

    public ICommand ShowWatchedCommand { get; }

    public ICommand ImportImdbRatingsCommand { get; }

    public ICommand ResetDatabaseCommand { get; }

    public ICommand RateSelectedTitleCommand { get; }

    public ICommand ToggleWatchlistCommand { get; }

    public ICommand OpenSelectedExternalLinkCommand => m_openSelectedExternalLinkCommand;

    public ICommand LoadMoreResultsCommand => m_loadMoreResultsCommand;

    public string SearchText
    {
        get => m_searchText;
        set
        {
            if (!SetField(ref m_searchText, value))
                return;

            ResetResultLimitAndRefresh();
        }
    }

    public DiscoveryKindOption SelectedKind
    {
        get => m_selectedKind;
        set
        {
            if (!SetField(ref m_selectedKind, value))
                return;

            ResetResultLimitAndRefresh();
        }
    }

    public LibraryItemSnapshotViewModel SelectedResult
    {
        get => m_selectedResult;
        set
        {
            if (!SetField(ref m_selectedResult, value))
                return;

            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedSubtitle));
            OnPropertyChanged(nameof(SelectedOverview));
            OnPropertyChanged(nameof(HasSelectedPoster));
            OnPropertyChanged(nameof(SelectedPersonalState));
            OnPropertyChanged(nameof(RecommendationSummary));
            OnPropertyChanged(nameof(HasSelectedRating));
            OnPropertyChanged(nameof(SelectedRatingLabel));
            OnPropertyChanged(nameof(HasSelectedPublicRating));
            OnPropertyChanged(nameof(SelectedPublicRatingLabel));
            OnPropertyChanged(nameof(HasSelectedRuntime));
            OnPropertyChanged(nameof(SelectedRuntimeLabel));
            OnPropertyChanged(nameof(IsSelectedOnWatchlist));
            OnPropertyChanged(nameof(CanToggleWatchlist));
            OnPropertyChanged(nameof(WatchlistButtonLabel));
            OnPropertyChanged(nameof(WatchlistButtonIcon));
            OnPropertyChanged(nameof(WatchlistButtonToolTip));
            OnPropertyChanged(nameof(CanOpenSelectedExternalLink));
            OnPropertyChanged(nameof(SelectedExternalLinkToolTip));
            m_openSelectedExternalLinkCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(Star1Glyph));
            OnPropertyChanged(nameof(Star2Glyph));
            OnPropertyChanged(nameof(Star3Glyph));
            OnPropertyChanged(nameof(Star4Glyph));
            OnPropertyChanged(nameof(Star5Glyph));
            _ = RefreshSelectedTitleDetailsAsync();
            _ = LoadSelectedPosterAsync();
        }
    }

    public string StatusText
    {
        get => m_statusText;
        private set => SetField(ref m_statusText, value);
    }

    public bool IsBusy
    {
        get => m_isBusy;
        private set => SetField(ref m_isBusy, value);
    }

    public bool CanLoadMore
    {
        get => m_canLoadMore;
        private set
        {
            if (!SetField(ref m_canLoadMore, value))
                return;

            m_loadMoreResultsCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDiscoveryMode => CurrentMode == LibraryViewMode.Discover;

    public bool IsWatchlistMode => CurrentMode == LibraryViewMode.Watchlist;

    public bool IsWatchedMode => CurrentMode == LibraryViewMode.Watched;

    private LibraryViewMode CurrentMode
    {
        get => m_currentMode;
        set
        {
            if (!SetField(ref m_currentMode, value))
                return;

            OnPropertyChanged(nameof(IsDiscoveryMode));
            OnPropertyChanged(nameof(IsWatchlistMode));
            OnPropertyChanged(nameof(IsWatchedMode));
            OnPropertyChanged(nameof(SearchWatermark));
            OnPropertyChanged(nameof(ResultsHeading));
            ResetResultLimitAndRefresh();
        }
    }

    public string SearchWatermark =>
        CurrentMode switch
        {
            LibraryViewMode.Watched => "Filter your watched titles by title...",
            LibraryViewMode.Watchlist => "Filter the films and shows you've pinned for later...",
            _ => "Search for something to watch or rate..."
        };

    public string ResultsHeading =>
        CurrentMode switch
        {
            LibraryViewMode.Watched => "WATCHED AND RATED",
            LibraryViewMode.Watchlist => "PINNED TO WATCH",
            _ => "BROWSE MOVIES"
        };

    public string SelectedTitle => SelectedResult?.Title ?? "Select a movie";

    public string SelectedSubtitle =>
        SelectedResult?.Subtitle ??
        CurrentMode switch
        {
            LibraryViewMode.Watched => "Your watched-title details will appear here.",
            LibraryViewMode.Watchlist => "Pinned titles you want to come back to will appear here.",
            _ => "Search for something specific or browse popular picks."
        };

    public string SelectedOverview => SelectedResult?.Overview ?? "Movie details will appear here once you select a title.";

    public Bitmap SelectedPoster
    {
        get => m_selectedPoster;
        private set
        {
            if (!SetField(ref m_selectedPoster, value))
                return;

            OnPropertyChanged(nameof(HasSelectedPoster));
        }
    }

    public bool HasSelectedPoster => SelectedPoster != null;

    public string SelectedPersonalState =>
        SelectedResult?.PersonalState ??
        CurrentMode switch
        {
            LibraryViewMode.Watchlist => "Pin anything that looks interesting so you can come back to it later.",
            _ => "Give a movie 0 to 5 stars after you've watched it, and MovieG33k will treat that as part of your watched history."
        };

    public string RecommendationSummary =>
        CurrentMode switch
        {
            LibraryViewMode.Watched => "Your watched history and ratings will help MovieG33k learn what tends to work for you.",
            LibraryViewMode.Watchlist => "Pin likely candidates here first, then rate the ones you actually watch.",
            _ => "Search for something new, then pin or rate anything you want to keep."
        };

    public bool HasSelectedRating => SelectedResult?.Snapshot.Rating != null;

    public string SelectedRatingLabel =>
        SelectedResult?.Snapshot.Rating == null
            ? string.Empty
            : $"Your rating: {GetStarRating(SelectedResult.Snapshot.Rating)}/5";

    public bool HasSelectedPublicRating => SelectedResult?.Snapshot.Title.PublicRating != null;

    public string SelectedPublicRatingLabel =>
        SelectedResult?.Snapshot.Title.PublicRating == null
            ? string.Empty
            : $"TMDb community rating: {SelectedResult.Snapshot.Title.PublicRating / 2m:0.0}/5";

    public bool HasSelectedRuntime => TryGetSelectedRuntimeMinutes(out _);

    public string SelectedRuntimeLabel =>
        TryGetSelectedRuntimeMinutes(out var runtimeMinutes)
            ? $"Runtime: {FormatRuntime(runtimeMinutes)}"
            : string.Empty;

    public bool IsSelectedOnWatchlist => SelectedResult?.Snapshot.WatchlistEntry != null;

    public bool CanToggleWatchlist => SelectedResult?.Snapshot.Rating == null;

    public string WatchlistButtonLabel => IsSelectedOnWatchlist ? "Pinned" : "Pin to watch";

    public MaterialIconKind WatchlistButtonIcon =>
        IsSelectedOnWatchlist
            ? MaterialIconKind.PinOffOutline
            : MaterialIconKind.PinOutline;

    public string WatchlistButtonToolTip =>
        IsSelectedOnWatchlist
            ? "Remove this title from your pinned watchlist"
            : "Pin this title so you can come back to it later";

    public bool CanOpenSelectedExternalLink => GetSelectedExternalUri() != null;

    public string SelectedExternalLinkToolTip =>
        SelectedResult?.Snapshot.Title.Identifiers.ImdbId is not null
            ? "Open this title in IMDb"
            : SelectedResult?.Snapshot.Title.Identifiers.TmdbId is not null
                ? "Open this title in TMDb"
                : "No external page is available for this title";

    public string Star1Glyph => GetStarGlyph(1);

    public string Star2Glyph => GetStarGlyph(2);

    public string Star3Glyph => GetStarGlyph(3);

    public string Star4Glyph => GetStarGlyph(4);

    public string Star5Glyph => GetStarGlyph(5);

    public Task RefreshAsync() => RefreshResultsAsync();

    public Task LoadMoreAsync() => LoadMoreResultsAsync();

    private async Task RefreshResultsAsync()
    {
        m_refreshCancellationTokenSource?.Cancel();
        m_refreshCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = m_refreshCancellationTokenSource.Token;
        var selectedCatalogKey =
            SelectedResult == null
                ? null
                : CatalogTitleKey.Create(SelectedResult.Snapshot.Title.Kind, SelectedResult.Snapshot.Title.Identifiers);

        try
        {
            IsBusy = true;

            var query = new DiscoveryQuery(SearchText, SelectedKind.Kind, m_regionCode, m_resultLimit);
            var result =
                CurrentMode switch
                {
                    LibraryViewMode.Watched => await m_discoveryWorkspaceService.GetWatchedAsync(query, cancellationToken),
                    LibraryViewMode.Watchlist => await m_discoveryWorkspaceService.GetWatchlistAsync(query, cancellationToken),
                    _ => await m_discoveryWorkspaceService.DiscoverAsync(query, cancellationToken)
                };

            if (cancellationToken.IsCancellationRequested)
                return;

            Results.Clear();
            m_enrichedDetailKeys.Clear();
            foreach (var item in result.Items)
                Results.Add(new LibraryItemSnapshotViewModel(item));

            CanLoadMore = result.Items.Count >= m_resultLimit;

            SelectedResult =
                Results.FirstOrDefault(item =>
                    selectedCatalogKey != null &&
                    string.Equals(
                        CatalogTitleKey.Create(item.Snapshot.Title.Kind, item.Snapshot.Title.Identifiers),
                        selectedCatalogKey,
                        StringComparison.OrdinalIgnoreCase))
                ?? Results.FirstOrDefault();
            StatusText = result.StatusText;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = $"Something went wrong: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsBusy = false;
        }
    }

    private void ResetResultLimitAndRefresh()
    {
        m_resultLimit = InitialResultLimit;
        CanLoadMore = false;
        _ = RefreshResultsAsync();
    }

    private async Task LoadMoreResultsAsync()
    {
        if (!CanLoadMore)
            return;

        m_resultLimit += ResultLimitIncrement;
        await RefreshResultsAsync();
    }

    private async Task LoadSelectedPosterAsync()
    {
        m_posterCancellationTokenSource?.Cancel();
        m_posterCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = m_posterCancellationTokenSource.Token;
        await Dispatcher.UIThread.InvokeAsync(() => SelectedPoster = null);

        var posterUrl = SelectedResult?.PosterUrl;
        if (string.IsNullOrWhiteSpace(posterUrl))
        {
            Logger.Instance.Warn($"No poster path is available for '{SelectedTitle}'.");
            return;
        }

        Logger.Instance.Info($"Loading poster for '{SelectedTitle}' from '{posterUrl}'.");

        try
        {
            await using var networkStream = await OpenPosterStreamAsync(posterUrl, cancellationToken);
            using var memoryStream = new MemoryStream();
            await networkStream.CopyToAsync(memoryStream, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            var posterBytes = memoryStream.ToArray();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var bitmapStream = new MemoryStream(posterBytes, writable: false);
                SelectedPoster = new Bitmap(bitmapStream);
            });
            Logger.Instance.Info($"Poster loaded for '{SelectedTitle}'.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => SelectedPoster = null);
            Logger.Instance.Exception($"Failed to load poster for '{SelectedTitle}' from '{posterUrl}'.", ex);
        }
    }

    private async Task RefreshSelectedTitleDetailsAsync()
    {
        m_selectedDetailsCancellationTokenSource?.Cancel();
        m_selectedDetailsCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = m_selectedDetailsCancellationTokenSource.Token;
        var selectedSnapshot = SelectedResult?.Snapshot;
        if (selectedSnapshot == null)
            return;

        var shouldRefreshDetails =
            selectedSnapshot.Title is MovieEntry { RuntimeMinutes: null or <= 0 } ||
            string.IsNullOrWhiteSpace(selectedSnapshot.Title.PosterPath);
        if (!shouldRefreshDetails)
            return;

        var catalogKey = CatalogTitleKey.Create(selectedSnapshot.Title.Kind, selectedSnapshot.Title.Identifiers);
        if (!m_enrichedDetailKeys.Add(catalogKey))
            return;

        try
        {
            Logger.Instance.Info($"Requesting richer details for '{selectedSnapshot.Title.Name}'.");
            var detailedSnapshot = await m_discoveryWorkspaceService.GetTitleDetailsAsync(selectedSnapshot.Title, cancellationToken);
            if (cancellationToken.IsCancellationRequested || detailedSnapshot == null)
                return;

            var detailedCatalogKey = CatalogTitleKey.Create(detailedSnapshot.Title.Kind, detailedSnapshot.Title.Identifiers);
            var replacementSnapshot = detailedSnapshot with
            {
                SourceLabel = ResolveDisplaySourceLabel(selectedSnapshot, detailedSnapshot)
            };
            var replacement = new LibraryItemSnapshotViewModel(replacementSnapshot);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingIndex = Results
                    .Select((item, index) => new { item, index })
                    .FirstOrDefault(entry =>
                        string.Equals(
                            CatalogTitleKey.Create(entry.item.Snapshot.Title.Kind, entry.item.Snapshot.Title.Identifiers),
                            detailedCatalogKey,
                            StringComparison.OrdinalIgnoreCase))
                    ?.index;
                if (existingIndex.HasValue)
                    Results[existingIndex.Value] = replacement;

                SelectedResult = replacement;
            });
            Logger.Instance.Info($"Updated cached details for '{detailedSnapshot.Title.Name}'.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Instance.Exception($"Failed to refresh detailed metadata for '{selectedSnapshot.Title.Name}'.", ex);
        }
    }

    private static async Task<Stream> OpenPosterStreamAsync(string posterUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(posterUrl))
            throw new InvalidOperationException("A poster URL or local path is required.");

        if (Path.IsPathRooted(posterUrl) && File.Exists(posterUrl))
            return File.OpenRead(posterUrl);

        if (Uri.TryCreate(posterUrl, UriKind.Absolute, out var posterUri) && posterUri.IsFile)
            return File.OpenRead(posterUri.LocalPath);

        using var response = await PosterHttpClient.GetAsync(posterUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode == 429)
            Logger.Instance.Warn($"TMDb image request returned HTTP 429 for '{posterUrl}'.");

        response.EnsureSuccessStatusCode();
        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var memoryStream = new MemoryStream();
        await networkStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private static HttpClient CreatePosterHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MovieG33k/1.0");
        return client;
    }

    private static string ResolveDisplaySourceLabel(LibraryItemSnapshot previousSnapshot, LibraryItemSnapshot refreshedSnapshot)
    {
        if (!string.IsNullOrWhiteSpace(refreshedSnapshot?.SourceLabel) &&
            !string.Equals(refreshedSnapshot.SourceLabel, "TMDb", StringComparison.OrdinalIgnoreCase))
            return refreshedSnapshot.SourceLabel;

        if (!string.IsNullOrWhiteSpace(previousSnapshot?.SourceLabel))
            return previousSnapshot.SourceLabel;

        return refreshedSnapshot?.Rating != null ||
               refreshedSnapshot?.WatchState != null ||
               refreshedSnapshot?.WatchlistEntry != null
            ? "In your library"
            : "Search hit";
    }

    private Uri GetSelectedExternalUri()
    {
        var identifiers = SelectedResult?.Snapshot.Title.Identifiers;
        if (identifiers == null)
            return null;

        if (!string.IsNullOrWhiteSpace(identifiers.ImdbId))
            return new Uri($"https://www.imdb.com/title/{identifiers.ImdbId}/", UriKind.Absolute);

        if (identifiers.TmdbId is int tmdbId)
        {
            var mediaPath = SelectedResult?.Snapshot.Title.Kind == TitleKind.TvShow ? "tv" : "movie";
            return new Uri($"https://www.themoviedb.org/{mediaPath}/{tmdbId}", UriKind.Absolute);
        }

        return null;
    }

    private bool TryGetSelectedRuntimeMinutes(out int runtimeMinutes)
    {
        if (SelectedResult?.Snapshot.Title is MovieEntry { RuntimeMinutes: > 0 } movieEntry)
        {
            runtimeMinutes = movieEntry.RuntimeMinutes.Value;
            return true;
        }

        runtimeMinutes = 0;
        return false;
    }

    private static string FormatRuntime(int runtimeMinutes)
    {
        var hours = runtimeMinutes / 60;
        var minutes = runtimeMinutes % 60;

        return hours <= 0
            ? $"{minutes}m"
            : minutes == 0
                ? $"{hours}h"
                : $"{hours}h {minutes}m";
    }

    private async Task RateSelectedTitleAsync(object parameter)
    {
        if (SelectedResult == null || !TryGetStars(parameter, out var stars))
            return;

        await m_discoveryWorkspaceService.SaveRatingAsync(SelectedResult.Snapshot.Title, stars);
        await RefreshResultsAsync();
    }

    private async Task ToggleWatchlistAsync()
    {
        if (SelectedResult == null || !CanToggleWatchlist)
            return;

        await m_discoveryWorkspaceService.SetWatchlistStateAsync(
            SelectedResult.Snapshot.Title,
            !IsSelectedOnWatchlist);

        await RefreshResultsAsync();
    }

    private void OpenSelectedExternalLink()
    {
        var externalUri = GetSelectedExternalUri();
        if (externalUri == null)
            return;

        try
        {
            Logger.Instance.Info($"Opening external link '{externalUri}'.");
            externalUri.Open();
        }
        catch (Exception ex)
        {
            Logger.Instance.Exception($"Failed to open external link '{externalUri}'.", ex);
            m_dialogService.ShowMessage("Couldn't open link", ex.Message);
        }
    }

    private async Task ImportImdbRatingsAsync()
    {
        var selectedFile = await m_dialogService.ShowFileOpenAsync("Import IMDb ratings", "IMDb CSV", ["*.csv"]);
        if (selectedFile == null)
            return;

        var progressToken = new ProgressToken();
        progressToken.Progress = 0;

        var progress = new Progress<ImdbImportProgress>(update =>
        {
            if (update == null || update.TotalRows <= 0)
                return;

            progressToken.Progress = (int)Math.Round(update.ProcessedRows * 85d / update.TotalRows, MidpointRounding.AwayFromZero);
        });

        ImdbImportResult importResult;
        int appliedCount;
        using (m_dialogService.ShowBusy("Importing your IMDb ratings...", progressToken))
        {
            importResult = await m_imdbImportService.ImportAsync(selectedFile, progress);
            progressToken.Progress = 92;
            appliedCount = await m_discoveryWorkspaceService.ApplyImportAsync(importResult);
            progressToken.Progress = 100;
        }

        if (appliedCount > 0)
        {
            CurrentMode = LibraryViewMode.Watched;
            SearchText = string.Empty;
        }

        await RefreshResultsAsync();

        m_dialogService.ShowMessage(
            "IMDb import complete",
            BuildImportSummary(importResult, appliedCount));
    }

    private async Task ResetDatabaseAsync()
    {
        var confirmed = await ConfirmAsync(
            "Reset local database?",
            "This will clear your cached titles, watched history, and ratings stored by MovieG33k on this device.");
        if (!confirmed)
            return;

        await m_discoveryWorkspaceService.ResetLibraryAsync();
        CurrentMode = LibraryViewMode.Discover;
        SearchText = string.Empty;
        await RefreshResultsAsync();
        m_dialogService.ShowMessage("Database reset", "MovieG33k has cleared its local database.");
    }

    private Task<bool> ConfirmAsync(string title, string detail)
    {
        var completionSource = new TaskCompletionSource<bool>();
        m_dialogService.Warn(title, detail, "Cancel", "Reset", result => completionSource.TrySetResult(result));
        return completionSource.Task;
    }

    private string GetStarGlyph(int starIndex) => CurrentStarRating >= starIndex ? "★" : "☆";

    private int CurrentStarRating =>
        SelectedResult?.Snapshot.Rating == null
            ? 0
            : GetStarRating(SelectedResult.Snapshot.Rating);

    private static int GetStarRating(UserRating rating) => (int)Math.Round(rating.ScoreOutOfTen / 2.0, MidpointRounding.AwayFromZero);

    private static bool TryGetStars(object parameter, out int stars)
    {
        switch (parameter)
        {
            case int intValue when intValue is >= 0 and <= 5:
                stars = intValue;
                return true;
            case string text when int.TryParse(text, out var parsedValue) && parsedValue is >= 0 and <= 5:
                stars = parsedValue;
                return true;
            default:
                stars = 0;
                return false;
        }
    }

    private static string BuildImportSummary(ImdbImportResult importResult, int appliedCount)
    {
        if (importResult.Items.Count == 0)
            return "That file didn't contain any IMDb rating rows to bring in.";

        if (appliedCount > 0)
        {
            return appliedCount == 1
                ? "Imported 1 rated title into your watched list."
                : $"Imported {appliedCount} rated title(s) into your watched list.";
        }

        return importResult.ResolvedItemCount == 0
            ? $"Read {importResult.Items.Count} IMDb row(s), but couldn't match them to titles yet, so nothing was added."
            : $"Read {importResult.Items.Count} IMDb row(s), but nothing new was added to your watched list.";
    }
}
