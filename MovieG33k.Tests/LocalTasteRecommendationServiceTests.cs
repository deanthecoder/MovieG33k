// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MovieG33k.Core.Models;
using MovieG33k.Core.Services;

namespace MovieG33k.Tests;

public sealed class LocalTasteRecommendationServiceTests
{
    [Test]
    public async Task GetRecommendationsAsyncExcludesWatchedPinnedAndRatedTitles()
    {
        var alien = new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction", "Horror"], "en", 117, 8.5m);
        var robocop = new MovieEntry(new TitleIdentifiers(5548, "tt0093870"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en", 102, 7.4m);
        var hackers = new MovieEntry(new TitleIdentifiers(10428, "tt0113243"), "Hackers", "Hackers", "Three", new DateOnly(1995, 9, 15), null, null, ["Crime", "Thriller"], "en", 107, 6.3m);

        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Aliens", 10, 1986, ["Science Fiction", "Action"])
            ],
            snapshotsByKey: new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [CatalogTitleKey.Create(robocop.Kind, robocop.Identifiers)] = new(robocop, WatchlistEntry: new WatchlistEntry(robocop.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow)),
                [CatalogTitleKey.Create(hackers.Kind, hackers.Identifiers)] = new(hackers, new UserRating(hackers.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow))
            });
        var tmdbClient = new FakeTmdbMetadataClient([alien, robocop, hackers]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Alien" }));
    }

    [Test]
    public async Task GetRecommendationsAsyncLetsGenreQueryFilterTheResults()
    {
        var alien = new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction", "Horror"], "en", 117, 8.5m);
        var robocop = new MovieEntry(new TitleIdentifiers(5548, "tt0093870"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en", 102, 7.4m);
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Scream", 8, 1996, ["Horror"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([alien, robocop]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery("horror", TitleKind.Movie, "GB", 20));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Alien" }));
    }

    private sealed class FakeLibraryRepository : ILibraryRepository
    {
        private readonly IReadOnlyList<RatedTitleInsight> m_ratedInsights;
        private readonly Dictionary<string, LibraryItemSnapshot> m_snapshotsByKey;

        public FakeLibraryRepository(
            IReadOnlyList<RatedTitleInsight> ratedInsights,
            IReadOnlyDictionary<string, LibraryItemSnapshot> snapshotsByKey = null)
        {
            m_ratedInsights = ratedInsights;
            m_snapshotsByKey = snapshotsByKey == null
                ? new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LibraryItemSnapshot>(snapshotsByKey, StringComparer.OrdinalIgnoreCase);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default)
        {
            foreach (var title in titles)
            {
                var key = CatalogTitleKey.Create(title.Kind, title.Identifiers);
                if (!m_snapshotsByKey.ContainsKey(key))
                    m_snapshotsByKey[key] = new LibraryItemSnapshot(title);
            }

            return Task.CompletedTask;
        }

        public Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteWatchlistEntryAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertProviderAvailabilityAsync(TitleIdentifiers identifiers, TitleKind kind, IReadOnlyList<ProviderAvailability> providerAvailabilities, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LibraryItemSnapshot>>(
                m_snapshotsByKey
                    .Where(pair => catalogKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(TitleKind kind, CancellationToken cancellationToken = default) => Task.FromResult(m_ratedInsights);
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTmdbMetadataClient : ITmdbMetadataClient
    {
        private readonly IReadOnlyList<CatalogTitle> m_results;

        public FakeTmdbMetadataClient(IReadOnlyList<CatalogTitle> results) => m_results = results;

        public bool IsConfigured => true;
        public string RegionCode => "GB";
        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) => Task.FromResult(m_results);
        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogTitle>>(m_results.Where(result => result.Kind == kind).Take(maxResults).ToArray());
        public Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogTitle>>(m_results.Where(result => result.Kind == kind).Take(maxResults).ToArray());
        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) => Task.FromResult(m_results.FirstOrDefault());
        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) => Task.FromResult(m_results.FirstOrDefault());
    }
}
