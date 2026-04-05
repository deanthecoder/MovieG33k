// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using DTC.Core;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;
using MovieG33k.Imdb.Services;
using MovieG33k.ViewModels;
using MovieG33k.Views;

namespace MovieG33k.Tests;

[TestFixture]
public sealed class ReadmeScreenshotTests
{
    private static readonly DirectoryInfo RepositoryDirectory = new(GetRepositoryRootPath());
    private static readonly DirectoryInfo ImageDirectory = new(Path.Combine(RepositoryDirectory.FullName, "img"));
    private static readonly DirectoryInfo PosterAssetDirectory = new(Path.Combine(RepositoryDirectory.FullName, "MovieG33k.Tests", "Assets", "ReadmePosters"));

    [Test]
    public async Task CaptureReadmeScreenshots()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

        await session.Dispatch(async () =>
        {
            ImageDirectory.Create();

            await CaptureBrowseScreenshotAsync();
            await CaptureRecommendedScreenshotAsync();
            await CaptureInsightsScreenshotAsync();
            await CaptureWatchlistScreenshotAsync();
            await CaptureWatchedScreenshotAsync();

            return 0;
        }, CancellationToken.None);
    }

    private static async Task CaptureBrowseScreenshotAsync()
    {
        var sampleData = CreateSampleData();
        var viewModel = CreateViewModel(sampleData);
        var window = CreateWindow(viewModel);

        window.Show();

        try
        {
            await viewModel.RefreshAsync();
            viewModel.SelectedResult = viewModel.Results.First(item => item.Title == "RoboCop");
            await WaitForSelectedPosterAsync(viewModel);
            SaveScreenshot(window, "browse-movies.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CaptureWatchlistScreenshotAsync()
    {
        var sampleData = CreateSampleData();
        var viewModel = CreateViewModel(sampleData);
        var window = CreateWindow(viewModel);

        window.Show();

        try
        {
            viewModel.ShowWatchlistCommand.Execute(null);
            await viewModel.RefreshAsync();
            viewModel.SelectedResult = viewModel.Results.First(item => item.Title == "Hackers");
            await WaitForSelectedPosterAsync(viewModel);
            SaveScreenshot(window, "pinned-movies.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CaptureInsightsScreenshotAsync()
    {
        var sampleData = CreateSampleData();
        var viewModel = CreateViewModel(sampleData);
        var window = CreateWindow(viewModel);

        window.Show();

        try
        {
            viewModel.ShowInsightsCommand.Execute(null);
            await viewModel.RefreshAsync();
            await WaitForRenderAsync();
            SaveScreenshot(window, "insights.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CaptureRecommendedScreenshotAsync()
    {
        var sampleData = CreateSampleData();
        var viewModel = CreateViewModel(sampleData);
        var window = CreateWindow(viewModel);

        window.Show();

        try
        {
            viewModel.ShowRecommendedCommand.Execute(null);
            await viewModel.RefreshAsync();
            viewModel.SelectedResult = viewModel.Results.First(item => item.Title == "RoboCop");
            await WaitForSelectedPosterAsync(viewModel);
            SaveScreenshot(window, "recommended-movies.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CaptureWatchedScreenshotAsync()
    {
        var sampleData = CreateSampleData();
        var viewModel = CreateViewModel(sampleData);
        var window = CreateWindow(viewModel);

        window.Show();

        try
        {
            viewModel.ShowWatchedCommand.Execute(null);
            await viewModel.RefreshAsync();
            viewModel.SelectedResult = viewModel.Results.First(item => item.Title == "Flight of the Navigator");
            await WaitForSelectedPosterAsync(viewModel);
            SaveScreenshot(window, "watched-movies.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindowViewModel CreateViewModel(SampleData sampleData)
    {
        var repository = new ScreenshotLibraryRepository(sampleData);
        var tmdbClient = new ScreenshotTmdbMetadataClient(sampleData.Browse.Select(snapshot => snapshot.Title).ToArray());
        return new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new ScreenshotRecommendationService(sampleData.Recommended.Select(snapshot => new RecommendationCandidate(
                snapshot.Title,
                10,
                "Recommended",
                snapshot.Title.Genres?.Take(2).ToArray() ?? Array.Empty<string>())).ToArray()),
            new ImdbCsvImportService(tmdbClient),
            new ScreenshotDialogService());
    }

    private static MainWindow CreateWindow(MainWindowViewModel viewModel) =>
        new(viewModel)
        {
            Width = 1220,
            Height = 780
        };

    private static SampleData CreateSampleData()
    {
        var robocopPosterPath = new FileInfo(Path.Combine(PosterAssetDirectory.FullName, "robocop.jpg")).FullName;
        var navigatorPosterPath = new FileInfo(Path.Combine(PosterAssetDirectory.FullName, "flight-of-the-navigator.jpg")).FullName;
        var hackersPosterPath = new FileInfo(Path.Combine(PosterAssetDirectory.FullName, "hackers.jpg")).FullName;

        Assert.That(File.Exists(robocopPosterPath), Is.True, "Expected the RoboCop poster fixture to exist.");
        Assert.That(File.Exists(navigatorPosterPath), Is.True, "Expected the Flight of the Navigator poster fixture to exist.");
        Assert.That(File.Exists(hackersPosterPath), Is.True, "Expected the Hackers poster fixture to exist.");

        var navigator = new MovieEntry(
            new TitleIdentifiers(11357, "tt0091059"),
            "Flight of the Navigator",
            "Flight of the Navigator",
            "A boy vanishes for years, returns without aging, and becomes tangled up with a wisecracking alien spacecraft.",
            new DateOnly(1986, 8, 1),
            navigatorPosterPath,
            null,
            ["Family", "Adventure"],
            "en",
            90,
            7.0m);
        var robocop = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "A murdered Detroit police officer is rebuilt as a cybernetic law enforcer and starts reclaiming his humanity.",
            new DateOnly(1987, 7, 17),
            robocopPosterPath,
            null,
            ["Action", "Science Fiction"],
            "en",
            102,
            7.4m);
        var hackers = new MovieEntry(
            new TitleIdentifiers(10428, "tt0113243"),
            "Hackers",
            "Hackers",
            "A group of young hackers uncover a corporate conspiracy and turn their digital mischief into a high-stakes rescue mission.",
            new DateOnly(1995, 9, 15),
            hackersPosterPath,
            null,
            ["Crime", "Thriller"],
            "en",
            107,
            6.3m);

        var browse = new[]
        {
            new LibraryItemSnapshot(robocop, SourceLabel: "Top match"),
            new LibraryItemSnapshot(hackers, WatchlistEntry: new WatchlistEntry(hackers.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow.AddDays(-2), 1), SourceLabel: "Pinned"),
            new LibraryItemSnapshot(navigator, new UserRating(navigator.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow.AddDays(-5)), new WatchState(navigator.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow.AddDays(-5)), SourceLabel: "In your library")
        };
        var recommended = new[]
        {
            new LibraryItemSnapshot(robocop, SourceLabel: "Recommended"),
            new LibraryItemSnapshot(hackers, SourceLabel: "Recommended")
        };
        var watchlist = new[]
        {
            new LibraryItemSnapshot(hackers, WatchlistEntry: new WatchlistEntry(hackers.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow.AddDays(-2), 2), SourceLabel: "Pinned"),
            new LibraryItemSnapshot(robocop, WatchlistEntry: new WatchlistEntry(robocop.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow.AddDays(-1), 1), SourceLabel: "Pinned")
        };
        var watched = new[]
        {
            new LibraryItemSnapshot(navigator, new UserRating(navigator.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow.AddDays(-5)), new WatchState(navigator.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow.AddDays(-5)), SourceLabel: "In your library"),
            new LibraryItemSnapshot(robocop, new UserRating(robocop.Identifiers, TitleKind.Movie, 10, DateTimeOffset.UtcNow.AddDays(-14)), new WatchState(robocop.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow.AddDays(-14)), SourceLabel: "In your library")
        };

        return new SampleData(browse, recommended, watchlist, watched);
    }

    private static void SaveScreenshot(Window window, string fileName)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, $"Expected a rendered frame for screenshot '{fileName}'.");

        var file = new FileInfo(Path.Combine(ImageDirectory.FullName, fileName));
        using var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None);
        frame!.Save(stream);
        file.Refresh();
        Assert.That(file.Exists, Is.True);
        Assert.That(file.Length, Is.GreaterThan(0L));
    }

    private static async Task WaitForRenderAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static async Task WaitForSelectedPosterAsync(MainWindowViewModel viewModel)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await WaitForRenderAsync();

            if (viewModel.HasSelectedPoster)
                return;

            await Task.Delay(150);
        }

        Assert.Fail($"Timed out waiting for the README screenshot poster to load for '{viewModel.SelectedTitle}'.");
    }

    private static string GetRepositoryRootPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MovieG33k.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the MovieG33k repository root.");
    }

    private sealed record SampleData(
        IReadOnlyList<LibraryItemSnapshot> Browse,
        IReadOnlyList<LibraryItemSnapshot> Recommended,
        IReadOnlyList<LibraryItemSnapshot> Watchlist,
        IReadOnlyList<LibraryItemSnapshot> Watched)
    {
        public IReadOnlyDictionary<string, LibraryItemSnapshot> ByCatalogKey =>
            Browse
                .Concat(Recommended)
                .Concat(Watchlist)
                .Concat(Watched)
                .GroupBy(snapshot => CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ScreenshotLibraryRepository : ILibraryRepository
    {
        private readonly SampleData m_sampleData;

        public ScreenshotLibraryRepository(SampleData sampleData) => m_sampleData = sampleData;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteWatchlistEntryAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertProviderAvailabilityAsync(TitleIdentifiers identifiers, TitleKind kind, IReadOnlyList<ProviderAvailability> providerAvailabilities, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(string query, TitleKind kind, int maxResults, string directorFilter = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Filter(m_sampleData.Browse, query, kind, maxResults, directorFilter));

        public Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LibraryItemSnapshot>>(
                catalogKeys
                    .Where(catalogKey => m_sampleData.ByCatalogKey.ContainsKey(catalogKey))
                    .ToDictionary(catalogKey => catalogKey, catalogKey => m_sampleData.ByCatalogKey[catalogKey], StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(Filter(m_sampleData.Watched, query, kind, maxResults, null));

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(Filter(m_sampleData.Watchlist, query, kind, maxResults, null));

        public Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RatedTitleInsight>>(
                m_sampleData.ByCatalogKey.Values
                    .Where(snapshot => snapshot.Title.Kind == kind && snapshot.Rating != null)
                    .Select(snapshot => new RatedTitleInsight(
                        CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers),
                        snapshot.Title.Name,
                        snapshot.Rating.ScoreOutOfTen,
                        snapshot.Title.ReleaseYear,
                        snapshot.Title.Genres ?? Array.Empty<string>()))
                    .ToArray());

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetRatedTitlesMissingMetadataAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(
                m_sampleData.ByCatalogKey.Values
                    .Where(snapshot => snapshot.Title.Kind == kind && snapshot.Rating != null)
                    .Take(maxResults)
                    .ToArray());

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetTitlesMissingMetadataAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(
                m_sampleData.ByCatalogKey.Values
                    .Where(snapshot => snapshot.Title.Kind == kind)
                    .Take(maxResults)
                    .ToArray());

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static IReadOnlyList<LibraryItemSnapshot> Filter(IReadOnlyList<LibraryItemSnapshot> source, string query, TitleKind kind, int maxResults, string directorFilter)
        {
            IEnumerable<LibraryItemSnapshot> filtered = source.Where(snapshot => snapshot.Title.Kind == kind);

            if (!string.IsNullOrWhiteSpace(directorFilter))
            {
                filtered = filtered.Where(snapshot =>
                    snapshot.Title.Directors?.Any(director => director.Contains(directorFilter, StringComparison.OrdinalIgnoreCase)) == true);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(snapshot =>
                    snapshot.Title.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (snapshot.Title.OriginalName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return filtered.Take(maxResults).ToArray();
        }
    }

    private sealed class ScreenshotTmdbMetadataClient : ITmdbMetadataClient
    {
        private readonly IReadOnlyList<CatalogTitle> m_titles;

        public ScreenshotTmdbMetadataClient(IReadOnlyList<CatalogTitle> titles) => m_titles = titles;

        public bool IsConfigured => true;

        public string RegionCode => "GB";

        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
        {
            IEnumerable<CatalogTitle> filtered = m_titles.Where(title => title.Kind == query.Kind);

            if (!string.IsNullOrWhiteSpace(query.Query))
            {
                filtered = filtered.Where(title =>
                    title.Name.Contains(query.Query, StringComparison.OrdinalIgnoreCase) ||
                    (title.OriginalName?.Contains(query.Query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return Task.FromResult<IReadOnlyList<CatalogTitle>>(filtered.Take(query.MaxResults).ToArray());
        }

        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>(m_titles.Where(title => title.Kind == kind).Take(maxResults).ToArray());

        public Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>(m_titles.Where(title => title.Kind == query.Kind).Take(query.MaxResults).ToArray());

        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, string titleName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                m_titles.FirstOrDefault(title =>
                    title.Kind == kind &&
                    (title.Identifiers.TmdbId == identifiers.TmdbId ||
                     string.Equals(title.Identifiers.ImdbId, identifiers.ImdbId, StringComparison.OrdinalIgnoreCase))));

        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_titles.FirstOrDefault(title =>
                title.Kind == kind &&
                string.Equals(title.Identifiers.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class ScreenshotRecommendationService : IRecommendationService
    {
        private readonly IReadOnlyList<RecommendationCandidate> m_results;

        public ScreenshotRecommendationService(IReadOnlyList<RecommendationCandidate> results) => m_results = results;

        public Task<IReadOnlyList<RecommendationCandidate>> GetRecommendationsAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
        {
            IEnumerable<RecommendationCandidate> filtered = m_results.Where(result => result.Title.Kind == query.Kind);

            if (!string.IsNullOrWhiteSpace(query.Query))
            {
                filtered = filtered.Where(result =>
                    result.Title.Name.Contains(query.Query, StringComparison.OrdinalIgnoreCase) ||
                    result.Title.Genres?.Any(genre => genre.Contains(query.Query, StringComparison.OrdinalIgnoreCase)) == true);
            }

            return Task.FromResult<IReadOnlyList<RecommendationCandidate>>(filtered.Take(query.MaxResults).ToArray());
        }
    }

    private sealed class ScreenshotDialogService : DTC.Core.UI.IDialogService
    {
        public void Warn(string message, string detail, string cancelButton, string actionButton, Action<bool> onClose, Material.Icons.MaterialIconKind? icon = null) => onClose(false);

        public void ShowMessage(string message, string detail, Material.Icons.MaterialIconKind? icon = null)
        {
        }

        public Task<string> ShowTextEntryAsync(string message, string detail, string initialValue = null, string watermark = null, string cancelButton = "Cancel", string actionButton = "OK", Material.Icons.MaterialIconKind? icon = null) =>
            Task.FromResult<string>(null);

        public Task<FileInfo> ShowFileOpenAsync(string title, string filterName, string[] filterExtensions) =>
            Task.FromResult<FileInfo>(null);

        public Task<FileInfo> ShowFileSaveAsync(string title, string defaultFileName, string filterName, string[] filterExtensions) =>
            Task.FromResult<FileInfo>(null);

        public Task<DirectoryInfo> SelectFolderAsync(string title, DirectoryInfo defaultFolder = null) =>
            Task.FromResult<DirectoryInfo>(null);

        public IDisposable ShowBusy(string message, ProgressToken progress) => null;
    }
}
