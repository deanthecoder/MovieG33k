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
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;
using MovieG33k.Imdb.Services;

namespace MovieG33k.Tests;

public sealed class ImdbCsvImportServiceTests
{
    [Test]
    public async Task ImportAsyncParsesCurrentImdbCsvShapeAndReportsProgress()
    {
        var csvFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv"));
        var progressUpdates = new List<ImdbImportProgress>();

        try
        {
            await File.WriteAllTextAsync(
                csvFile.FullName,
                "Const,Your Rating,Date Rated,Title,Original Title,URL,Title Type,IMDb Rating,Runtime (mins),Year,Genres,Num Votes,Release Date,Directors\n" +
                "tt15398776,9,2024-01-01,\"Oppenheimer\",\"Oppenheimer\",https://www.imdb.com/title/tt15398776,Movie,8.4,180,2023,\"Drama\",1000,2023-07-21,\"Christopher Nolan\"\n" +
                "tt0106179,8,2024-01-02,\"The X-Files\",\"The X Files\",https://www.imdb.com/title/tt0106179,TV Series,8.6,45,1993,\"Drama\",1000,1993-09-10,\n");

            var service = new ImdbCsvImportService(new FakeTmdbMetadataClient());
            var progress = new TrackingProgress(progressUpdates);

            var result = await service.ImportAsync(csvFile, progress);

            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(result.Items[0].ImdbId, Is.EqualTo("tt15398776"));
            Assert.That(result.Items[0].ResolvedTitle?.Name, Is.EqualTo("Oppenheimer"));
            Assert.That(result.Items[1].Kind, Is.EqualTo(TitleKind.TvShow));
            Assert.That(result.Items[1].ResolvedTitle, Is.TypeOf<TvShowEntry>());
            Assert.That(result.ResolvedItemCount, Is.EqualTo(2));
            Assert.That(progressUpdates, Is.Not.Empty);
            Assert.That(progressUpdates[0].TotalRows, Is.EqualTo(2));
            Assert.That(progressUpdates[^1].ProcessedRows, Is.EqualTo(2));
        }
        finally
        {
            if (csvFile.Exists)
                csvFile.Delete();
        }
    }

    private sealed class FakeTmdbMetadataClient : ITmdbMetadataClient
    {
        public bool IsConfigured => true;

        public string RegionCode => "GB";

        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>([]);

        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>([]);

        public Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>([]);

        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogTitle>(null);

        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogTitle>(
                kind == TitleKind.Movie
                    ? new MovieEntry(
                        new TitleIdentifiers(603692, imdbId),
                        "Oppenheimer",
                        "Oppenheimer",
                        "Resolved from IMDb.",
                        new DateOnly(2023, 7, 21),
                        null,
                        null,
                        ["Drama"],
                        "en")
                    : new TvShowEntry(
                        new TitleIdentifiers(4087, imdbId),
                        "The X-Files",
                        "The X Files",
                        "Resolved from IMDb.",
                        new DateOnly(1993, 9, 10),
                        null,
                        null,
                        ["Drama"],
                        "en"));
    }

    private sealed class TrackingProgress : IProgress<ImdbImportProgress>
    {
        private readonly List<ImdbImportProgress> m_updates;
        private readonly object m_gate = new();

        public TrackingProgress(List<ImdbImportProgress> updates) => m_updates = updates;

        public void Report(ImdbImportProgress value)
        {
            lock (m_gate)
                m_updates.Add(value);
        }
    }
}
