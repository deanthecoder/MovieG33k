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
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Windows.Input;
using Avalonia.Media;
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
    private static readonly IReadOnlyList<Color> GenreSharePalette =
    [
        Color.Parse("#53C7FF"),
        Color.Parse("#7CEB8B"),
        Color.Parse("#F4C94A"),
        Color.Parse("#FF8F70"),
        Color.Parse("#B28CFF"),
        Color.Parse("#8FA3B8")
    ];
    private static readonly RecommendationFilterOption AnyGenreOption = new("Any genre", null);
    private static readonly IReadOnlyList<string> MovieGenres =
    [
        "Action", "Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama", "Family", "Fantasy", "History",
        "Horror", "Music", "Mystery", "Romance", "Science Fiction", "TV Movie", "Thriller", "War", "Western"
    ];
    private static readonly IReadOnlyList<string> TvGenres =
    [
        "Action & Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama", "Family", "Kids", "Mystery",
        "News", "Reality", "Sci-Fi & Fantasy", "Soap", "Talk", "War & Politics", "Western"
    ];
    private static readonly IReadOnlyList<RecommendationFilterOption> MovieAgeRatingOptions =
    [
        new("Any age rating", null),
        new("12+", "12+"),
        new("15+", "15+"),
        new("18+", "18+")
    ];

    private enum LibraryViewMode
    {
        Discover,
        Recommended,
        Watchlist,
        Watched,
        Insights
    }

    private static readonly HttpClient PosterHttpClient = CreatePosterHttpClient();
    private readonly DiscoveryWorkspaceService m_discoveryWorkspaceService;
    private readonly IRecommendationService m_recommendationService;
    private readonly IImdbImportService m_imdbImportService;
    private readonly IDialogService m_dialogService;
    private readonly IPosterCache m_posterCache;
    private readonly AsyncRelayCommand m_loadMoreResultsCommand;
    private readonly RelayCommand m_openSelectedExternalLinkCommand;
    private readonly string m_regionCode;
    private LibraryInsights m_insights;
    private string m_searchText;
    private DiscoveryKindOption m_selectedKind;
    private LibraryItemSnapshotViewModel m_selectedResult;
    private string m_statusText = "Loading movies...";
    private bool m_isBusy;
    private bool m_canLoadMore;
    private LibraryViewMode m_currentMode;
    private int m_resultLimit = InitialResultLimit;
    private IImage m_selectedPoster;
    private IReadOnlyList<RecommendationFilterOption> m_recommendationGenreOptions;
    private IReadOnlyList<RecommendationFilterOption> m_recommendationAgeRatingOptions;
    private RecommendationFilterOption m_selectedRecommendationGenreOption;
    private RecommendationFilterOption m_selectedRecommendationAgeRatingOption;
    private CancellationTokenSource m_refreshCancellationTokenSource;
    private CancellationTokenSource m_posterCancellationTokenSource;
    private CancellationTokenSource m_selectedDetailsCancellationTokenSource;
    private readonly HashSet<string> m_enrichedDetailKeys = new(StringComparer.OrdinalIgnoreCase);
    private string m_activeDirectorFilter;
    private bool m_suppressAutoRefresh;

    /// <summary>
    /// Creates a new main window view model.
    /// </summary>
    public MainWindowViewModel(
        DiscoveryWorkspaceService discoveryWorkspaceService,
        IRecommendationService recommendationService,
        IImdbImportService imdbImportService,
        IDialogService dialogService,
        IPosterCache posterCache = null,
        string regionCode = "GB")
    {
        m_discoveryWorkspaceService = discoveryWorkspaceService ?? throw new ArgumentNullException(nameof(discoveryWorkspaceService));
        m_recommendationService = recommendationService ?? throw new ArgumentNullException(nameof(recommendationService));
        m_imdbImportService = imdbImportService ?? throw new ArgumentNullException(nameof(imdbImportService));
        m_dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        m_posterCache = posterCache;
        m_regionCode = string.IsNullOrWhiteSpace(regionCode) ? "GB" : regionCode.Trim().ToUpperInvariant();

        KindOptions =
        [
            new DiscoveryKindOption("Movies", TitleKind.Movie),
            new DiscoveryKindOption("TV Shows", TitleKind.TvShow)
        ];
        m_selectedKind = KindOptions[0];
        m_currentMode = LibraryViewMode.Discover;
        Results = new ObservableCollection<LibraryItemSnapshotViewModel>();
        RatingDistribution = new ObservableCollection<InsightsBarViewModel>();
        RatingByDecade = new ObservableCollection<InsightsBarViewModel>();
        RatingByGenre = new ObservableCollection<InsightsBarViewModel>();
        GenreShareSlices = new ObservableCollection<InsightsPieSliceViewModel>();
        MostWatchedGenres = new ObservableCollection<InsightsBarViewModel>();
        m_recommendationGenreOptions = BuildGenreOptions(m_selectedKind.Kind);
        m_recommendationAgeRatingOptions = BuildAgeRatingOptions(m_selectedKind.Kind);
        m_selectedRecommendationGenreOption = m_recommendationGenreOptions[0];
        m_selectedRecommendationAgeRatingOption = m_recommendationAgeRatingOptions[0];

        ShowDiscoverCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Discover);
        ShowRecommendedCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Recommended);
        ShowWatchlistCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Watchlist);
        ShowWatchedCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Watched);
        ShowInsightsCommand = new RelayCommand(_ => CurrentMode = LibraryViewMode.Insights);
        ApplyDirectorFilterCommand = new RelayCommand(parameter => ApplyDirectorFilter(parameter as string));
        ClearDirectorFilterCommand = new RelayCommand(_ => ClearDirectorFilter(), _ => HasActiveDirectorFilter);
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

    public ICommand ShowRecommendedCommand { get; }

    public ICommand ShowWatchlistCommand { get; }

    public ICommand ShowWatchedCommand { get; }

    public ICommand ShowInsightsCommand { get; }

    public ICommand ApplyDirectorFilterCommand { get; }

    public ICommand ClearDirectorFilterCommand { get; }

    public ICommand ImportImdbRatingsCommand { get; }

    public ICommand ResetDatabaseCommand { get; }

    public ICommand RateSelectedTitleCommand { get; }

    public ICommand ToggleWatchlistCommand { get; }

    public ICommand OpenSelectedExternalLinkCommand => m_openSelectedExternalLinkCommand;

    public ICommand LoadMoreResultsCommand => m_loadMoreResultsCommand;

    public ObservableCollection<InsightsBarViewModel> RatingDistribution { get; }

    public ObservableCollection<InsightsBarViewModel> RatingByDecade { get; }

    public ObservableCollection<InsightsBarViewModel> RatingByGenre { get; }

    public ObservableCollection<InsightsPieSliceViewModel> GenreShareSlices { get; }

    public ObservableCollection<InsightsBarViewModel> MostWatchedGenres { get; }

    public IReadOnlyList<RecommendationFilterOption> RecommendationGenreOptions
    {
        get => m_recommendationGenreOptions;
        private set => SetField(ref m_recommendationGenreOptions, value);
    }

    public IReadOnlyList<RecommendationFilterOption> RecommendationAgeRatingOptions
    {
        get => m_recommendationAgeRatingOptions;
        private set => SetField(ref m_recommendationAgeRatingOptions, value);
    }

    public RecommendationFilterOption SelectedRecommendationGenreOption
    {
        get => m_selectedRecommendationGenreOption;
        set
        {
            if (!SetField(ref m_selectedRecommendationGenreOption, value))
                return;

            ResetResultLimitAndRefresh(selectFirstResult: true);
        }
    }

    public RecommendationFilterOption SelectedRecommendationAgeRatingOption
    {
        get => m_selectedRecommendationAgeRatingOption;
        set
        {
            if (!SetField(ref m_selectedRecommendationAgeRatingOption, value))
                return;

            ResetResultLimitAndRefresh(selectFirstResult: true);
        }
    }

    public string SearchText
    {
        get => m_searchText;
        set
        {
            if (!SetField(ref m_searchText, value))
                return;

            if (!m_suppressAutoRefresh)
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

            UpdateRecommendationFilterOptions();
            if (!m_suppressAutoRefresh)
                ResetResultLimitAndRefresh();
        }
    }

    public LibraryItemSnapshotViewModel SelectedResult
    {
        get => m_selectedResult;
        set
        {
            if (ReferenceEquals(m_selectedResult, value))
                return;

            if (m_selectedResult != null)
                m_selectedResult.PropertyChanged -= OnSelectedResultPropertyChanged;

            SetField(ref m_selectedResult, value);

            if (m_selectedResult != null)
                m_selectedResult.PropertyChanged += OnSelectedResultPropertyChanged;

            RefreshSelectedPresentationState();
            m_openSelectedExternalLinkCommand.RaiseCanExecuteChanged();
            _ = RefreshSelectedTitleDetailsAsync();
            _ = LoadSelectedPosterAsync();
        }
    }

    private void OnSelectedResultPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedResult))
            return;

        RefreshSelectedPresentationState();
    }

    private void RefreshSelectedPresentationState()
    {
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedSubtitle));
        OnPropertyChanged(nameof(SelectedOverview));
        OnPropertyChanged(nameof(SelectedPersonalState));
        OnPropertyChanged(nameof(HasSelectedRecommendationSignals));
        OnPropertyChanged(nameof(RecommendationSummary));
        OnPropertyChanged(nameof(HasSelectedRating));
        OnPropertyChanged(nameof(SelectedRatingLabel));
        OnPropertyChanged(nameof(HasSelectedPublicRating));
        OnPropertyChanged(nameof(SelectedPublicRatingLabel));
        OnPropertyChanged(nameof(HasSelectedRuntime));
        OnPropertyChanged(nameof(SelectedRuntimeLabel));
        OnPropertyChanged(nameof(SelectedDirectors));
        OnPropertyChanged(nameof(HasSelectedDirectors));
        OnPropertyChanged(nameof(IsSelectedOnWatchlist));
        OnPropertyChanged(nameof(CanToggleWatchlist));
        OnPropertyChanged(nameof(WatchlistButtonLabel));
        OnPropertyChanged(nameof(WatchlistButtonIcon));
        OnPropertyChanged(nameof(WatchlistButtonToolTip));
        OnPropertyChanged(nameof(CanOpenSelectedExternalLink));
        OnPropertyChanged(nameof(SelectedExternalLinkToolTip));
        OnPropertyChanged(nameof(Star1Glyph));
        OnPropertyChanged(nameof(Star2Glyph));
        OnPropertyChanged(nameof(Star3Glyph));
        OnPropertyChanged(nameof(Star4Glyph));
        OnPropertyChanged(nameof(Star5Glyph));
    }

    private void RefreshInsightsPresentationState()
    {
        OnPropertyChanged(nameof(HasInsights));
        OnPropertyChanged(nameof(InsightsEmptyState));
        OnPropertyChanged(nameof(TotalRatedValue));
        OnPropertyChanged(nameof(AverageRatingValue));
        OnPropertyChanged(nameof(TopDecadeValue));
        OnPropertyChanged(nameof(TopGenreValue));
        OnPropertyChanged(nameof(HasGenreShare));
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

    public bool IsRecommendedMode => CurrentMode == LibraryViewMode.Recommended;

    public bool IsWatchlistMode => CurrentMode == LibraryViewMode.Watchlist;

    public bool IsWatchedMode => CurrentMode == LibraryViewMode.Watched;

    public bool IsInsightsMode => CurrentMode == LibraryViewMode.Insights;

    public bool HasRecommendationFilters => IsRecommendedMode;

    public bool HasRecommendationAgeRatingFilter => IsRecommendedMode && SelectedKind?.Kind == TitleKind.Movie;

    public bool HasActiveDirectorFilter => !string.IsNullOrWhiteSpace(ActiveDirectorFilter);

    public string ActiveDirectorFilter
    {
        get => m_activeDirectorFilter;
        private set
        {
            if (!SetField(ref m_activeDirectorFilter, value))
                return;

            OnPropertyChanged(nameof(HasActiveDirectorFilter));
            OnPropertyChanged(nameof(SearchWatermark));
            OnPropertyChanged(nameof(ResultsHeading));
            (ClearDirectorFilterCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private LibraryViewMode CurrentMode
    {
        get => m_currentMode;
        set
        {
            var previousMode = m_currentMode;
            if (!SetField(ref m_currentMode, value))
                return;

            OnPropertyChanged(nameof(IsDiscoveryMode));
            OnPropertyChanged(nameof(IsRecommendedMode));
            OnPropertyChanged(nameof(IsWatchlistMode));
            OnPropertyChanged(nameof(IsWatchedMode));
            OnPropertyChanged(nameof(IsInsightsMode));
            OnPropertyChanged(nameof(HasRecommendationFilters));
            OnPropertyChanged(nameof(HasRecommendationAgeRatingFilter));
            OnPropertyChanged(nameof(SearchWatermark));
            OnPropertyChanged(nameof(ResultsHeading));

            if (previousMode != value)
                SelectedResult = null;

            if (!m_suppressAutoRefresh)
                ResetResultLimitAndRefresh();
        }
    }

    public string SearchWatermark =>
        CurrentMode switch
        {
            LibraryViewMode.Insights => "Insights are built from the titles you've rated.",
            LibraryViewMode.Recommended => "Filter recommendations by title or genre...",
            LibraryViewMode.Watched => "Filter your watched titles by title...",
            LibraryViewMode.Watchlist => "Filter the films and shows you've pinned for later...",
            _ when HasActiveDirectorFilter => $"Filter titles directed by {ActiveDirectorFilter}...",
            _ => "Search for something to watch or rate..."
        };

    public string ResultsHeading =>
        CurrentMode switch
        {
            LibraryViewMode.Insights => "INSIGHTS",
            LibraryViewMode.Recommended => "RECOMMENDED",
            LibraryViewMode.Watched => "WATCHED AND RATED",
            LibraryViewMode.Watchlist => "PINNED TO WATCH",
            _ when HasActiveDirectorFilter => $"DIRECTED BY {ActiveDirectorFilter?.ToUpperInvariant()}",
            _ => "BROWSE MOVIES"
        };

    public string SelectedTitle => SelectedResult?.Title ?? "Select a movie";

    public string SelectedSubtitle =>
        SelectedResult?.Snapshot != null
            ? LibraryItemSnapshotViewModel.BuildStableSubtitle(SelectedResult.Snapshot)
            :
        CurrentMode switch
        {
            LibraryViewMode.Recommended => "Pick a recommendation and see why it floated to the top.",
            LibraryViewMode.Watched => "Your watched-title details will appear here.",
            LibraryViewMode.Watchlist => "Pinned titles you want to come back to will appear here.",
            _ => "Search for something specific or browse popular picks."
        };

    public string SelectedOverview => SelectedResult?.Overview ?? "Movie details will appear here once you select a title.";

    public IImage SelectedPoster
    {
        get => m_selectedPoster;
        private set
        {
            var previousPoster = m_selectedPoster;
            if (!SetField(ref m_selectedPoster, value))
                return;

            (previousPoster as IDisposable)?.Dispose();

            OnPropertyChanged(nameof(HasSelectedPoster));
        }
    }

    public bool HasSelectedPoster => SelectedPoster != null;

    public string SelectedPersonalState =>
        SelectedResult?.PersonalState ??
        CurrentMode switch
        {
            LibraryViewMode.Recommended => "Your top genres and decades shape these picks.",
            LibraryViewMode.Watchlist => "Pin anything that looks interesting so you can come back to it later.",
            _ => "Give a movie 0 to 5 stars after you've watched it, and MovieG33k will treat that as part of your watched history."
        };

    public bool HasSelectedRecommendationSignals =>
        IsRecommendedMode &&
        !string.IsNullOrWhiteSpace(SelectedResult?.PersonalState);

    public string RecommendationSummary =>
        CurrentMode switch
        {
            LibraryViewMode.Recommended => "Recommendation signals are pulled from your ratings history and lightly balanced with community ratings.",
            LibraryViewMode.Watched => "Your watched history and ratings will help MovieG33k learn what tends to work for you.",
            LibraryViewMode.Watchlist => "Pin likely candidates here first, then rate the ones you actually watch.",
            _ => "Search for something new, then pin or rate anything you want to keep."
        };

    public bool HasInsights => m_insights?.TotalRatedTitles > 0;

    public string InsightsEmptyState =>
        SelectedKind?.Kind == TitleKind.TvShow
            ? "Rate a few TV shows and your charts will show up here."
            : "Rate a few movies and your charts will show up here.";

    public string TotalRatedValue => m_insights?.TotalRatedTitles.ToString() ?? "0";

    public string AverageRatingValue => m_insights == null || m_insights.TotalRatedTitles == 0 ? "0.0/5" : $"{m_insights.AverageRatingOutOfFive:0.0}/5";

    public string TopDecadeValue =>
        m_insights?.RatingByDecade?
            .OrderByDescending(bucket => bucket.AverageRatingOutOfFive)
            .ThenByDescending(bucket => bucket.TitleCount)
            .Select(bucket => $"{bucket.DecadeStartYear}s")
            .FirstOrDefault()
        ?? "N/A";

    public string TopGenreValue =>
        m_insights?.RatingByGenre?
            .OrderByDescending(bucket => bucket.TitleCount)
            .ThenByDescending(bucket => bucket.AverageRatingOutOfFive)
            .Select(bucket => bucket.Genre)
            .FirstOrDefault()
        ?? "N/A";

    public bool HasGenreShare => GenreShareSlices.Count > 0;

    public bool HasSelectedRating => SelectedResult?.Snapshot.Rating != null;

    public string SelectedRatingLabel =>
        SelectedResult?.Snapshot.Rating == null
            ? string.Empty
            : $"Your rating: {GetStarRating(SelectedResult.Snapshot.Rating)}/5";

    public bool HasSelectedPublicRating => SelectedResult?.Snapshot.Title.PublicRating != null;

    public string SelectedPublicRatingLabel =>
        SelectedResult?.Snapshot.Title.PublicRating == null
            ? string.Empty
            : $"Community rating: {SelectedResult.Snapshot.Title.PublicRating / 2m:0.0}/5";

    public bool HasSelectedRuntime => TryGetSelectedRuntimeMinutes(out _);

    public string SelectedRuntimeLabel =>
        TryGetSelectedRuntimeMinutes(out var runtimeMinutes)
            ? $"Runtime: {FormatRuntime(runtimeMinutes)}"
            : string.Empty;

    public IReadOnlyList<string> SelectedDirectors =>
        SelectedResult?.Snapshot.Title.Directors?
            .Where(director => !string.IsNullOrWhiteSpace(director) && !CatalogTitle.IsUnknownDirector(director))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? Array.Empty<string>();

    public bool HasSelectedDirectors => SelectedDirectors.Count > 0;

    public bool IsSelectedOnWatchlist => SelectedResult?.Snapshot.WatchlistEntry != null;

    public bool CanToggleWatchlist => SelectedResult?.Snapshot.Rating == null;

    public string WatchlistButtonLabel => IsSelectedOnWatchlist ? "Pinned" : "Add to watchlist";

    public MaterialIconKind WatchlistButtonIcon =>
        IsSelectedOnWatchlist
            ? MaterialIconKind.PinOffOutline
            : MaterialIconKind.PinOutline;

    public string WatchlistButtonToolTip =>
        IsSelectedOnWatchlist
            ? "Remove this title from your watchlist"
            : "Add this title to your watchlist so you can come back to it later";

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
        var selectedResultIndex = SelectedResult == null ? -1 : Results.IndexOf(SelectedResult);
        var selectedCatalogKey =
            SelectedResult == null
                ? null
                : CatalogTitleKey.Create(SelectedResult.Snapshot.Title.Kind, SelectedResult.Snapshot.Title.Identifiers);

        try
        {
            IsBusy = true;

            if (CurrentMode == LibraryViewMode.Insights)
            {
                await RefreshInsightsAsync(cancellationToken);
                return;
            }

            if (CurrentMode == LibraryViewMode.Recommended)
            {
                await RefreshRecommendationsAsync(cancellationToken, selectedCatalogKey, selectedResultIndex);
                return;
            }

            var query = new DiscoveryQuery(
                SearchText,
                SelectedKind.Kind,
                m_regionCode,
                m_resultLimit,
                DirectorFilter: CurrentMode == LibraryViewMode.Discover ? ActiveDirectorFilter : null);
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

            SelectedResult = ResolvePreferredSelection(selectedCatalogKey, selectedResultIndex);
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

    private async Task RefreshRecommendationsAsync(CancellationToken cancellationToken, string selectedCatalogKey, int selectedResultIndex)
    {
        var query = new DiscoveryQuery(
            SearchText,
            SelectedKind.Kind,
            m_regionCode,
            m_resultLimit,
            SelectedRecommendationGenreOption?.Value,
            HasRecommendationAgeRatingFilter ? SelectedRecommendationAgeRatingOption?.Value : null);
        var candidates = await m_recommendationService.GetRecommendationsAsync(query, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return;

        Results.Clear();
        CanLoadMore = candidates.Count >= m_resultLimit;
        m_enrichedDetailKeys.Clear();
        foreach (var candidate in candidates)
            Results.Add(CreateRecommendationRow(candidate));

        var mediaType = query.Kind == TitleKind.Movie ? "movies" : "TV shows";
        StatusText =
            candidates.Count == 0
                ? string.IsNullOrWhiteSpace(query.Query)
                    ? $"No {mediaType} matched the current recommendation filters yet."
                    : $"No recommended {mediaType} matched \"{query.Query}\" and the current filters."
                : string.IsNullOrWhiteSpace(query.Query)
                    ? $"Recommended {mediaType} based on your ratings."
                    : $"Recommended {mediaType} matching \"{query.Query}\".";

        SelectedResult = ResolvePreferredSelection(selectedCatalogKey, selectedResultIndex);
    }

    private LibraryItemSnapshotViewModel ResolvePreferredSelection(string selectedCatalogKey, int selectedResultIndex)
    {
        var matchedSelection =
            Results.FirstOrDefault(item =>
                selectedCatalogKey != null &&
                string.Equals(
                    CatalogTitleKey.Create(item.Snapshot.Title.Kind, item.Snapshot.Title.Identifiers),
                    selectedCatalogKey,
                    StringComparison.OrdinalIgnoreCase));
        if (matchedSelection != null)
            return matchedSelection;

        if (Results.Count == 0)
            return null;

        if (selectedResultIndex >= 0)
            return Results[Math.Min(selectedResultIndex, Results.Count - 1)];

        return Results.FirstOrDefault();
    }

    private async Task RefreshInsightsAsync(CancellationToken cancellationToken)
    {
        var insights = await m_discoveryWorkspaceService.GetInsightsAsync(SelectedKind.Kind, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return;

        m_insights = insights;
        Results.Clear();
        SelectedResult = null;
        CanLoadMore = false;
        m_enrichedDetailKeys.Clear();

        ReplaceInsightBars(
            RatingDistribution,
            insights.RatingDistribution.Select(bucket => new InsightsBarViewModel(
                $"{bucket.Stars}★",
                bucket.TitleCount.ToString(),
                bucket.TitleCount == 1 ? "1 title" : $"{bucket.TitleCount} titles",
                bucket.TitleCount)));

        ReplaceInsightBars(
            RatingByDecade,
            insights.RatingByDecade.Select(bucket => new InsightsBarViewModel(
                $"{bucket.DecadeStartYear}s",
                $"{bucket.AverageRatingOutOfFive:0.0}/5",
                bucket.TitleCount == 1 ? "1 title" : $"{bucket.TitleCount} titles",
                bucket.AverageRatingOutOfFive)));

        ReplaceInsightBars(
            RatingByGenre,
            insights.RatingByGenre.Select(bucket => new InsightsBarViewModel(
                bucket.Genre,
                $"{bucket.AverageRatingOutOfFive:0.0}/5",
                bucket.TitleCount == 1 ? "1 title" : $"{bucket.TitleCount} titles",
                bucket.AverageRatingOutOfFive)));

        ReplaceInsightBars(
            MostWatchedGenres,
            insights.RatingByGenre
                .OrderByDescending(bucket => bucket.TitleCount)
                .ThenByDescending(bucket => bucket.AverageRatingOutOfFive)
                .Take(6)
                .Select(bucket => new InsightsBarViewModel(
                    bucket.Genre,
                    bucket.TitleCount == 1 ? "1 title" : $"{bucket.TitleCount} titles",
                    $"{CalculateGenreShare(bucket.TitleCount, insights.RatingByGenre):0}% of genre tags",
                    bucket.TitleCount)));

        ReplacePieSlices(GenreShareSlices, insights.RatingByGenre);

        RefreshInsightsPresentationState();
        var mediaType = SelectedKind.Kind == TitleKind.Movie ? "movies" : "TV shows";
        StatusText =
            insights.TotalRatedTitles == 0
                ? $"No rated {mediaType} yet."
                : $"Stats from {insights.TotalRatedTitles} rated {mediaType}.";
    }

    private static void ReplaceInsightBars(
        ObservableCollection<InsightsBarViewModel> target,
        IEnumerable<InsightsBarViewModel> source)
    {
        target.Clear();
        var items = source?.ToArray() ?? Array.Empty<InsightsBarViewModel>();
        var maxValue = items.Length == 0 ? 0 : items.Max(item => item.Percent);

        foreach (var item in items)
        {
            target.Add(item with
            {
                Percent = maxValue <= 0 ? 0 : item.Percent / maxValue * 100d
            });
        }
    }

    private static void ReplacePieSlices(
        ObservableCollection<InsightsPieSliceViewModel> target,
        IReadOnlyList<GenreRatingBucket> source)
    {
        target.Clear();
        var sortedBuckets = source?
            .OrderByDescending(bucket => bucket.TitleCount)
            .ThenBy(bucket => bucket.Genre)
            .ToList() ?? [];

        if (sortedBuckets.Count == 0)
            return;

        var buckets = sortedBuckets
            .Take(8)
            .ToList();

        var totalCount = sortedBuckets.Sum(bucket => bucket.TitleCount);
        var remainingCount = sortedBuckets.Skip(8).Sum(bucket => bucket.TitleCount);
        var remainingShare = CalculateGenreShare(remainingCount, totalCount);

        if (remainingCount > 0 && remainingShare <= 28d)
        {
            buckets.Add(new GenreRatingBucket("Other", remainingCount, 0));
        }

        if (totalCount <= 0)
            return;

        const double size = 180d;
        const double radius = 78d;
        const double innerRadius = 34d;
        var center = size / 2d;
        var angle = -90d;

        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index];
            var sweep = bucket.TitleCount / (double)totalCount * 360d;
            var fill = new SolidColorBrush(GenreSharePalette[index % GenreSharePalette.Count]);
            var pathData = CreateDonutSlicePath(center, center, radius, innerRadius, angle, sweep);

            target.Add(new InsightsPieSliceViewModel(
                bucket.Genre,
                $"{CalculateGenreShare(bucket.TitleCount, totalCount):0}%",
                bucket.TitleCount == 1 ? "1 tag" : $"{bucket.TitleCount} tags",
                pathData,
                fill));

            angle += sweep;
        }
    }

    private static double CalculateGenreShare(int count, IReadOnlyList<GenreRatingBucket> buckets)
    {
        var total = buckets?.Sum(bucket => bucket.TitleCount) ?? 0;
        return CalculateGenreShare(count, total);
    }

    private static double CalculateGenreShare(int count, int totalCount) =>
        totalCount <= 0 ? 0d : count / (double)totalCount * 100d;

    private static string CreateDonutSlicePath(double centerX, double centerY, double outerRadius, double innerRadius, double startAngleDegrees, double sweepAngleDegrees)
    {
        if (sweepAngleDegrees <= 0d)
            return string.Empty;

        if (sweepAngleDegrees >= 359.999d)
            sweepAngleDegrees = 359.999d;

        var startOuter = ToPoint(centerX, centerY, outerRadius, startAngleDegrees);
        var endOuter = ToPoint(centerX, centerY, outerRadius, startAngleDegrees + sweepAngleDegrees);
        var startInner = ToPoint(centerX, centerY, innerRadius, startAngleDegrees);
        var endInner = ToPoint(centerX, centerY, innerRadius, startAngleDegrees + sweepAngleDegrees);
        var isLargeArc = sweepAngleDegrees > 180d ? 1 : 0;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"M {startOuter.x:0.###},{startOuter.y:0.###} " +
            $"A {outerRadius:0.###},{outerRadius:0.###} 0 {isLargeArc} 1 {endOuter.x:0.###},{endOuter.y:0.###} " +
            $"L {endInner.x:0.###},{endInner.y:0.###} " +
            $"A {innerRadius:0.###},{innerRadius:0.###} 0 {isLargeArc} 0 {startInner.x:0.###},{startInner.y:0.###} Z");
    }

    private static (double x, double y) ToPoint(double centerX, double centerY, double radius, double angleDegrees)
    {
        var radians = Math.PI * angleDegrees / 180d;
        return (
            centerX + radius * Math.Cos(radians),
            centerY + radius * Math.Sin(radians));
    }

    private static LibraryItemSnapshotViewModel CreateRecommendationRow(RecommendationCandidate candidate)
    {
        var snapshot = new LibraryItemSnapshot(candidate.Title, SourceLabel: "Recommended");
        var badgeText = candidate.Score >= 10d ? "Top pick" : null;
        var personalState = candidate.Signals?.Count > 0
            ? string.Join(" • ", candidate.Signals)
            : candidate.Rationale;
        var subtitle = LibraryItemSnapshotViewModel.BuildStableSubtitle(snapshot);
        return new LibraryItemSnapshotViewModel(snapshot, subtitle, personalState, badgeText);
    }

    private void ResetResultLimitAndRefresh(bool selectFirstResult = false)
    {
        m_resultLimit = InitialResultLimit;
        CanLoadMore = false;
        if (selectFirstResult)
            SelectedResult = null;

        _ = RefreshResultsAsync();
    }

    private void ApplyDirectorFilter(string director)
    {
        if (string.IsNullOrWhiteSpace(director))
            return;

        m_suppressAutoRefresh = true;
        try
        {
            CurrentMode = LibraryViewMode.Discover;
            SelectedKind = KindOptions.First(option => option.Kind == TitleKind.Movie);
            SearchText = string.Empty;
            ActiveDirectorFilter = director.Trim();
        }
        finally
        {
            m_suppressAutoRefresh = false;
        }

        ResetResultLimitAndRefresh(selectFirstResult: true);
    }

    private void ClearDirectorFilter()
    {
        if (!HasActiveDirectorFilter)
            return;

        ActiveDirectorFilter = null;
        ResetResultLimitAndRefresh(selectFirstResult: true);
    }

    private void UpdateRecommendationFilterOptions()
    {
        RecommendationGenreOptions = BuildGenreOptions(SelectedKind.Kind);
        RecommendationAgeRatingOptions = BuildAgeRatingOptions(SelectedKind.Kind);

        m_selectedRecommendationGenreOption = RecommendationGenreOptions
            .FirstOrDefault(option => string.Equals(option.Value, m_selectedRecommendationGenreOption?.Value, StringComparison.OrdinalIgnoreCase))
            ?? RecommendationGenreOptions[0];
        m_selectedRecommendationAgeRatingOption = RecommendationAgeRatingOptions
            .FirstOrDefault(option => string.Equals(option.Value, m_selectedRecommendationAgeRatingOption?.Value, StringComparison.OrdinalIgnoreCase))
            ?? RecommendationAgeRatingOptions[0];

        OnPropertyChanged(nameof(RecommendationGenreOptions));
        OnPropertyChanged(nameof(RecommendationAgeRatingOptions));
        OnPropertyChanged(nameof(SelectedRecommendationGenreOption));
        OnPropertyChanged(nameof(SelectedRecommendationAgeRatingOption));
        OnPropertyChanged(nameof(HasRecommendationAgeRatingFilter));
    }

    private static IReadOnlyList<RecommendationFilterOption> BuildGenreOptions(TitleKind kind) =>
        new[] { AnyGenreOption }
            .Concat((kind == TitleKind.Movie ? MovieGenres : TvGenres).Select(genre => new RecommendationFilterOption(genre, genre)))
            .ToArray();

    private static IReadOnlyList<RecommendationFilterOption> BuildAgeRatingOptions(TitleKind kind) =>
        kind == TitleKind.Movie
            ? MovieAgeRatingOptions
            : [new RecommendationFilterOption("Any age rating", null)];

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

        var posterUrl = SelectedResult?.PosterUrl;
        if (string.IsNullOrWhiteSpace(posterUrl))
        {
            Logger.Instance.Warn($"No poster path is available for '{SelectedTitle}'.");
            await Dispatcher.UIThread.InvokeAsync(() => SelectedPoster = null);
            return;
        }

        Logger.Instance.Info($"Loading poster for '{SelectedTitle}' from '{posterUrl}'.");

        try
        {
            if (m_posterCache != null &&
                Uri.TryCreate(posterUrl, UriKind.Absolute, out var posterUri) &&
                !posterUri.IsFile)
            {
                var cachedPosterFile = await m_posterCache.GetOrAddAsync(
                    posterUrl,
                    loader: posterCacheCancellationToken => OpenPosterStreamAsync(posterUrl, posterCacheCancellationToken),
                    cancellationToken);

                if (cachedPosterFile == null || cancellationToken.IsCancellationRequested)
                    return;

                await using var cachedPosterStream = cachedPosterFile.OpenRead();
                await Dispatcher.UIThread.InvokeAsync(() => SelectedPoster = new Bitmap(cachedPosterStream));
            }
            else
            {
                await using var networkStream = await OpenPosterStreamAsync(posterUrl, cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() => SelectedPoster = new Bitmap(networkStream));
            }

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

        var shouldRefreshDetails = ShouldRefreshSelectedTitleDetails(selectedSnapshot.Title);
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
                    Results[existingIndex.Value].UpdateFromSnapshot(replacementSnapshot);
                else if (SelectedResult != null &&
                         string.Equals(
                             CatalogTitleKey.Create(SelectedResult.Snapshot.Title.Kind, SelectedResult.Snapshot.Title.Identifiers),
                             detailedCatalogKey,
                             StringComparison.OrdinalIgnoreCase))
                    SelectedResult.UpdateFromSnapshot(replacementSnapshot);

                _ = LoadSelectedPosterAsync();
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

    private static bool ShouldRefreshSelectedTitleDetails(CatalogTitle title) =>
        !title.HasResolvedPosterPath ||
        (title.Kind == TitleKind.Movie && RequiresReleasedMovieMetadata(title) && string.IsNullOrWhiteSpace(title.AgeRating)) ||
        (title.Kind == TitleKind.Movie && !title.HasResolvedDirectors);

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

    private static bool RequiresReleasedMovieMetadata(CatalogTitle title) =>
        title.Kind == TitleKind.Movie &&
        (!title.ReleaseDate.HasValue || title.ReleaseDate.Value <= DateOnly.FromDateTime(DateTime.UtcNow));

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

        var progressToken = new ProgressToken
        {
            Progress = 0
        };

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
