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
using DTC.Core;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;
using MovieG33k.Imdb.Services;
using MovieG33k.ViewModels;

namespace MovieG33k.Tests;

public sealed class MainWindowViewModelTests
{
    [Test]
    public async Task RefreshAsyncPreservesSelectedResultWhenItStillExists()
    {
        var titleA = new MovieEntry(
            new TitleIdentifiers(1, "tt001"),
            "Alien",
            "Alien",
            "One",
            new DateOnly(1979, 5, 25),
            null,
            null,
            ["Science Fiction"],
            "en");
        var titleB = new MovieEntry(
            new TitleIdentifiers(2, "tt002"),
            "RoboCop",
            "RoboCop",
            "Two",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en");
        var repository = new FakeLibraryRepository([
            new LibraryItemSnapshot(titleA, SourceLabel: "Local"),
            new LibraryItemSnapshot(titleB, SourceLabel: "Local")
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[1];

        await viewModel.RefreshAsync();

        Assert.That(viewModel.SelectedResult, Is.Not.Null);
        Assert.That(viewModel.SelectedResult.Title, Is.EqualTo("RoboCop"));
    }

    [Test]
    public async Task LoadMoreAsyncExpandsTheVisibleResultCount()
    {
        var titles = Enumerable.Range(1, 120)
            .Select(index => new LibraryItemSnapshot(
                new MovieEntry(
                    new TitleIdentifiers(index, $"tt{index:0000000}"),
                    $"Movie {index}",
                    $"Movie {index}",
                    $"Overview {index}",
                    new DateOnly(2000, 1, 1).AddDays(index),
                    null,
                    null,
                    ["Drama"],
                    "en"),
                SourceLabel: "Local"))
            .ToArray();
        var repository = new FakeLibraryRepository(titles);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();

        Assert.That(viewModel.Results, Has.Count.EqualTo(100));
        Assert.That(viewModel.CanLoadMore, Is.True);

        await viewModel.LoadMoreAsync();

        Assert.That(viewModel.Results, Has.Count.EqualTo(120));
        Assert.That(viewModel.CanLoadMore, Is.False);
    }

    [Test]
    public async Task SelectingATitleKeepsItsExistingDisplayLabelAfterDetailRefresh()
    {
        var initialTitle = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "Short summary.",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en");
        var detailedTitle = initialTitle with
        {
            RuntimeMinutes = 102,
            PosterPath = "/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg",
            AgeRating = "R"
        };

        var repository = new FakeLibraryRepository([
            new LibraryItemSnapshot(initialTitle, SourceLabel: "Search hit")
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([detailedTitle]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();
        var selectedResult = viewModel.Results[0];
        viewModel.SelectedResult = selectedResult;

        await WaitForAsync(() => viewModel.HasSelectedRuntime);

        Assert.That(viewModel.SelectedResult, Is.SameAs(selectedResult));
        Assert.That(viewModel.SelectedResult.SourceLabel, Is.EqualTo("Search hit"));
    }

    [Test]
    public async Task SelectingATitleRefreshesDirectorsWhenOtherCoreDetailsAlreadyExist()
    {
        var initialTitle = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "Short summary.",
            new DateOnly(1987, 7, 17),
            "/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg",
            null,
            ["Action"],
            "en",
            102,
            7.4m,
            "18");
        var detailedTitle = initialTitle with
        {
            Directors = ["Paul Verhoeven"]
        };

        var repository = new FakeLibraryRepository([
            new LibraryItemSnapshot(initialTitle, SourceLabel: "Search hit")
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([detailedTitle]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[0];

        await WaitForAsync(() =>
            viewModel.SelectedResult?.Snapshot.Title.Directors?.Contains("Paul Verhoeven") == true);

        Assert.That(viewModel.SelectedResult?.Snapshot.Title.Directors, Does.Contain("Paul Verhoeven"));
    }

    [Test]
    public async Task SelectedExternalLinkPrefersImdbWhenAvailable()
    {
        var title = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "Short summary.",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en");
        var repository = new FakeLibraryRepository([
            new LibraryItemSnapshot(title, SourceLabel: "Search hit")
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[0];

        Assert.That(viewModel.CanOpenSelectedExternalLink, Is.True);
        Assert.That(viewModel.SelectedExternalLinkToolTip, Is.EqualTo("Open this title in IMDb"));
        Assert.That(viewModel.OpenSelectedExternalLinkCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task InsightsModeBuildsStatsFromRatedTitles()
    {
        var titles =
            new[]
            {
                new LibraryItemSnapshot(
                    new MovieEntry(new TitleIdentifiers(1, "tt001"), "RoboCop", "RoboCop", "One", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en"),
                    new UserRating(new TitleIdentifiers(1, "tt001"), TitleKind.Movie, 10, DateTimeOffset.UtcNow)),
                new LibraryItemSnapshot(
                    new MovieEntry(new TitleIdentifiers(2, "tt002"), "Aliens", "Aliens", "Two", new DateOnly(1986, 7, 18), null, null, ["Action", "Science Fiction"], "en"),
                    new UserRating(new TitleIdentifiers(2, "tt002"), TitleKind.Movie, 8, DateTimeOffset.UtcNow)),
                new LibraryItemSnapshot(
                    new MovieEntry(new TitleIdentifiers(3, "tt003"), "Hackers", "Hackers", "Three", new DateOnly(1995, 9, 15), null, null, ["Crime", "Thriller"], "en"),
                    new UserRating(new TitleIdentifiers(3, "tt003"), TitleKind.Movie, 6, DateTimeOffset.UtcNow))
            };
        var repository = new FakeLibraryRepository(titles);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        viewModel.ShowInsightsCommand.Execute(null);
        await viewModel.RefreshAsync();

        Assert.That(viewModel.IsInsightsMode, Is.True);
        Assert.That(viewModel.TotalRatedValue, Is.EqualTo("3"));
        Assert.That(viewModel.AverageRatingValue, Is.EqualTo("4.0/5"));
        Assert.That(viewModel.RatingDistribution.Any(item => item.Label == "5★" && item.ValueText == "1"), Is.True);
        Assert.That(viewModel.RatingByGenre.First().Label, Is.EqualTo("Science Fiction").Or.EqualTo("Action"));
        Assert.That(viewModel.HasGenreShare, Is.True);
        Assert.That(viewModel.GenreShareSlices.Count, Is.GreaterThan(0));
        Assert.That(viewModel.MostWatchedGenres.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task RecommendedModeShowsRecommendationSignals()
    {
        var title = new MovieEntry(
            new TitleIdentifiers(348, "tt0078748"),
            "Alien",
            "Alien",
            "A deep-space horror classic.",
            new DateOnly(1979, 5, 25),
            null,
            null,
            ["Science Fiction", "Horror"],
            "en",
            117,
            8.5m);
        var recommendation = new RecommendationCandidate(
            title,
            12.3d,
            "Science Fiction • 1970s • Highly rated",
            ["Science Fiction", "1970s", "Highly rated"]);
        var repository = new FakeLibraryRepository([]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new FakeRecommendationService([recommendation]),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        viewModel.ShowRecommendedCommand.Execute(null);
        await viewModel.RefreshAsync();

        Assert.That(viewModel.IsRecommendedMode, Is.True);
        Assert.That(viewModel.Results, Has.Count.EqualTo(1));
        Assert.That(viewModel.Results[0].PersonalState, Is.EqualTo("Science Fiction • 1970s • Highly rated"));
        Assert.That(viewModel.SelectedTitle, Is.EqualTo("Alien"));
    }

    [Test]
    public async Task RecommendedModeSelectsTheNextResultWhenTheCurrentOneDisappears()
    {
        var alien = new MovieEntry(
            new TitleIdentifiers(348, "tt0078748"),
            "Alien",
            "Alien",
            "One",
            new DateOnly(1979, 5, 25),
            null,
            null,
            ["Science Fiction"],
            "en");
        var robocop = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "Two",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en");
        var hackers = new MovieEntry(
            new TitleIdentifiers(10428, "tt0113243"),
            "Hackers",
            "Hackers",
            "Three",
            new DateOnly(1995, 9, 15),
            null,
            null,
            ["Crime"],
            "en");

        var recommendationService = new FakeRecommendationService(
        [
            new RecommendationCandidate(alien, 9, "Recommended", ["Science Fiction"]),
            new RecommendationCandidate(robocop, 8, "Recommended", ["Action"]),
            new RecommendationCandidate(hackers, 7, "Recommended", ["Crime"])
        ]);

        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(new FakeLibraryRepository([]), new FakeTmdbMetadataClient([])),
            recommendationService,
            new ImdbCsvImportService(new FakeTmdbMetadataClient([])),
            new FakeDialogService());

        viewModel.ShowRecommendedCommand.Execute(null);
        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[1];

        recommendationService.Results =
        [
            new RecommendationCandidate(alien, 9, "Recommended", ["Science Fiction"]),
            new RecommendationCandidate(hackers, 7, "Recommended", ["Crime"])
        ];

        await viewModel.RefreshAsync();

        Assert.That(viewModel.SelectedResult, Is.Not.Null);
        Assert.That(viewModel.SelectedResult.Title, Is.EqualTo("Hackers"));
    }

    [Test]
    public async Task SwitchingToRecommendedModeSelectsTheFirstRecommendation()
    {
        var alien = new MovieEntry(
            new TitleIdentifiers(348, "tt0078748"),
            "Alien",
            "Alien",
            "One",
            new DateOnly(1979, 5, 25),
            null,
            null,
            ["Science Fiction"],
            "en");
        var robocop = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "Two",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en");
        var hackers = new MovieEntry(
            new TitleIdentifiers(10428, "tt0113243"),
            "Hackers",
            "Hackers",
            "Three",
            new DateOnly(1995, 9, 15),
            null,
            null,
            ["Crime"],
            "en");

        var repository = new FakeLibraryRepository([
            new LibraryItemSnapshot(alien, SourceLabel: "Local"),
            new LibraryItemSnapshot(robocop, SourceLabel: "Local")
        ]);
        var recommendationService = new FakeRecommendationService(
        [
            new RecommendationCandidate(hackers, 10, "Recommended", ["Crime"]),
            new RecommendationCandidate(robocop, 9, "Recommended", ["Action"])
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            recommendationService,
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[1];

        viewModel.ShowRecommendedCommand.Execute(null);
        await viewModel.RefreshAsync();

        Assert.That(viewModel.IsRecommendedMode, Is.True);
        Assert.That(viewModel.SelectedResult, Is.Not.Null);
        Assert.That(viewModel.SelectedResult.Title, Is.EqualTo("Hackers"));
    }

    [Test]
    public async Task ChangingRecommendationGenreFilterSelectsTheFirstResult()
    {
        var alien = new MovieEntry(
            new TitleIdentifiers(348, "tt0078748"),
            "Alien",
            "Alien",
            "One",
            new DateOnly(1979, 5, 25),
            null,
            null,
            ["Science Fiction", "Horror"],
            "en");
        var hackers = new MovieEntry(
            new TitleIdentifiers(10428, "tt0113243"),
            "Hackers",
            "Hackers",
            "Three",
            new DateOnly(1995, 9, 15),
            null,
            null,
            ["Crime"],
            "en");
        var recommendationService = new FakeRecommendationService(
        [
            new RecommendationCandidate(alien, 10, "Recommended", ["Science Fiction"]),
            new RecommendationCandidate(hackers, 9, "Recommended", ["Crime"])
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(new FakeLibraryRepository([]), tmdbClient),
            recommendationService,
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        viewModel.ShowRecommendedCommand.Execute(null);
        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[1];

        recommendationService.Results =
        [
            new RecommendationCandidate(hackers, 9, "Recommended", ["Crime"]),
            new RecommendationCandidate(alien, 8, "Recommended", ["Science Fiction"])
        ];

        viewModel.SelectedRecommendationGenreOption = viewModel.RecommendationGenreOptions.First(option => option.DisplayName == "Horror");
        await WaitForAsync(() => viewModel.SelectedResult?.Title == "Hackers");

        Assert.That(viewModel.SelectedResult, Is.Not.Null);
        Assert.That(viewModel.SelectedResult.Title, Is.EqualTo("Hackers"));
    }

    private sealed class FakeLibraryRepository : ILibraryRepository
    {
        private readonly List<LibraryItemSnapshot> m_searchResults;
        private readonly Dictionary<string, LibraryItemSnapshot> m_snapshotsByKey;

        public FakeLibraryRepository(IReadOnlyList<LibraryItemSnapshot> searchResults)
        {
            m_searchResults = searchResults.ToList();
            m_snapshotsByKey = m_searchResults.ToDictionary(
                snapshot => CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers),
                snapshot => snapshot,
                StringComparer.OrdinalIgnoreCase);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default)
        {
            foreach (var title in titles)
            {
                var catalogKey = CatalogTitleKey.Create(title.Kind, title.Identifiers);
                if (m_snapshotsByKey.TryGetValue(catalogKey, out var existingSnapshot))
                {
                    var updatedSnapshot = existingSnapshot with { Title = title };
                    m_snapshotsByKey[catalogKey] = updatedSnapshot;

                    var index = m_searchResults.FindIndex(snapshot =>
                        string.Equals(
                            CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers),
                            catalogKey,
                            StringComparison.OrdinalIgnoreCase));
                    if (index >= 0)
                        m_searchResults[index] = updatedSnapshot;
                }
                else
                {
                    var newSnapshot = new LibraryItemSnapshot(title);
                    m_snapshotsByKey[catalogKey] = newSnapshot;
                    m_searchResults.Add(newSnapshot);
                }
            }

            return Task.CompletedTask;
        }

        public Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteWatchlistEntryAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertProviderAvailabilityAsync(TitleIdentifiers identifiers, TitleKind kind, IReadOnlyList<ProviderAvailability> providerAvailabilities, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(m_searchResults.Where(snapshot => snapshot.Title.Kind == kind).Take(maxResults).ToArray());

        public Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LibraryItemSnapshot>>(
                m_snapshotsByKey
                    .Where(pair => catalogKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(m_searchResults.Take(maxResults).ToArray());

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(m_searchResults.Take(maxResults).ToArray());

        public Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RatedTitleInsight>>(
                m_searchResults
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
                m_searchResults
                    .Where(snapshot => snapshot.Title.Kind == kind && snapshot.Rating != null)
                    .Take(maxResults)
                    .ToArray());

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        Assert.Fail("Timed out waiting for the view model to refresh.");
    }

    private sealed class FakeTmdbMetadataClient : ITmdbMetadataClient
    {
        private readonly IReadOnlyList<CatalogTitle> m_results;

        public FakeTmdbMetadataClient(IReadOnlyList<CatalogTitle> results) => m_results = results;

        public bool IsConfigured => true;

        public string RegionCode => "GB";

        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results);

        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results);

        public Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>(m_results.Where(result => result.Kind == query.Kind).Take(query.MaxResults).ToArray());

        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results.FirstOrDefault());

        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results.FirstOrDefault());
    }

    private sealed class FakeRecommendationService : IRecommendationService
    {
        public FakeRecommendationService(IReadOnlyList<RecommendationCandidate> results) => Results = results;

        public IReadOnlyList<RecommendationCandidate> Results { get; set; }

        public Task<IReadOnlyList<RecommendationCandidate>> GetRecommendationsAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results);
    }

    private sealed class FakeDialogService : DTC.Core.UI.IDialogService
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
