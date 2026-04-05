// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Net;
using System.Net.Http;
using System.Threading;
using MovieG33k.Core.Models;
using MovieG33k.Tmdb.Models;
using MovieG33k.Tmdb.Services;

namespace MovieG33k.Tests;

public sealed class TmdbMetadataClientTests
{
    [Test]
    public async Task SearchAsyncFallsBackToStubResultsWhenCredentialsAreMissing()
    {
        var client = new TmdbMetadataClient(new HttpClient(), new TmdbOptions
        {
            AccessToken = null,
            ApiKey = null,
            RegionCode = "GB"
        });

        var results = await client.SearchAsync(new DiscoveryQuery("oppenheimer", TitleKind.Movie, "GB", 10));

        Assert.That(results, Is.Not.Empty);
        Assert.That(results[0].Name, Does.Contain("Oppenheimer").IgnoreCase);
    }

    [Test]
    public async Task SearchAsyncReturnsAlienMatchesWhenCredentialsAreMissing()
    {
        var client = new TmdbMetadataClient(new HttpClient(), new TmdbOptions
        {
            AccessToken = null,
            ApiKey = null,
            RegionCode = "GB"
        });

        var results = await client.SearchAsync(new DiscoveryQuery("Alien", TitleKind.Movie, "GB", 10));

        Assert.That(results.Select(result => result.Name), Does.Contain("Alien"));
    }

    [Test]
    public async Task ResolveImdbIdAsyncUsesStubCatalogWhenCredentialsAreMissing()
    {
        var client = new TmdbMetadataClient(new HttpClient(), new TmdbOptions
        {
            AccessToken = null,
            ApiKey = null,
            RegionCode = "GB"
        });

        var result = await client.ResolveImdbIdAsync("tt0903747", TitleKind.TvShow);

        Assert.That(result, Is.Not.Null);
        Assert.That(result?.Name, Is.EqualTo("Breaking Bad"));
    }

    [Test]
    public async Task SearchAsyncUsesBearerAccessTokenWhenConfigured()
    {
        var handler = new CapturingMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            ApiKey = "legacy-api-key",
            RegionCode = "GB"
        });

        var results = await client.SearchAsync(new DiscoveryQuery("Alien", TitleKind.Movie, "GB", 10));

        Assert.That(results, Is.Not.Empty);
        Assert.That(handler.LastAuthorizationScheme, Is.EqualTo("Bearer"));
        Assert.That(handler.LastAuthorizationParameter, Is.EqualTo("private-access-token"));
        Assert.That(handler.LastRequestUri, Does.Not.Contain("api_key="));
        Assert.That(results[0].PublicRating, Is.EqualTo(8.5m));
    }

    [Test]
    public async Task GetTitleDetailsAsyncMapsMovieRuntimeWhenConfigured()
    {
        var handler = new DetailsMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB"
        });

        var result = await client.GetTitleDetailsAsync(new TitleIdentifiers(5548, "tt0093870"), TitleKind.Movie);

        Assert.That(result, Is.TypeOf<MovieEntry>());
        Assert.That(((MovieEntry)result).RuntimeMinutes, Is.EqualTo(102));
        Assert.That(result.Identifiers.ImdbId, Is.EqualTo("tt0093870"));
        Assert.That(result.Genres, Does.Contain("Action"));
        Assert.That(result.AgeRating, Is.EqualTo("R"));
        Assert.That(result.Directors, Does.Contain("Paul Verhoeven"));
        Assert.That(handler.LastRequestUri, Does.Contain("append_to_response=release_dates%2Ccredits%2Cexternal_ids"));
    }

    [Test]
    public async Task GetTitleDetailsAsyncMarksMovieAgeRatingAsUnknownWhenNoCertificationIsReturned()
    {
        var handler = new DetailsWithoutCertificationMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB"
        });

        var result = await client.GetTitleDetailsAsync(new TitleIdentifiers(214198, "tt0070607"), TitleKind.Movie);

        Assert.That(result.AgeRating, Is.EqualTo(CatalogTitle.UnknownAgeRating));
    }

    [Test]
    public async Task ResolveImdbIdAsyncFetchesFullDetailsAfterTheInitialFindResult()
    {
        var handler = new FindThenDetailsMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB"
        });

        var result = await client.ResolveImdbIdAsync("tt0093870", TitleKind.Movie);

        Assert.That(result, Is.TypeOf<MovieEntry>());
        Assert.That(result.Identifiers.ImdbId, Is.EqualTo("tt0093870"));
        Assert.That(result.Identifiers.TmdbId, Is.EqualTo(5548));
        Assert.That(result.Genres, Does.Contain("Action"));
        Assert.That(((MovieEntry)result).RuntimeMinutes, Is.EqualTo(102));
        Assert.That(result.Directors, Does.Contain("Paul Verhoeven"));
        Assert.That(handler.RequestUris, Has.Count.EqualTo(2));
        Assert.That(handler.RequestUris[0], Does.Contain("/3/find/tt0093870"));
        Assert.That(handler.RequestUris[1], Does.Contain("/3/movie/5548"));
    }

    [Test]
    public async Task GetTrendingAsyncRequestsMultiplePagesWhenMoreResultsAreNeeded()
    {
        var handler = new PagedTrendingMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB"
        });

        var results = await client.GetTrendingAsync(TitleKind.Movie, 35);

        Assert.That(results, Has.Count.EqualTo(35));
        Assert.That(handler.RequestUris, Has.Count.EqualTo(2));
        Assert.That(handler.RequestUris[0], Does.Contain("page=1"));
        Assert.That(handler.RequestUris[1], Does.Contain("page=2"));
    }

    [Test]
    public async Task GetDiscoverAsyncBlendsQualityAndPopularityDiscoverQueries()
    {
        var handler = new PagedTrendingMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB",
            Language = "en-GB"
        });

        var results = await client.GetDiscoverAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 25));

        Assert.That(results, Has.Count.EqualTo(25));
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("/3/discover/movie", StringComparison.Ordinal)), Is.True);
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("sort_by=vote_average.desc", StringComparison.Ordinal)), Is.True);
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("vote_count.gte=1500", StringComparison.Ordinal)), Is.True);
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("sort_by=popularity.desc", StringComparison.Ordinal)), Is.True);
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("vote_count.gte=400", StringComparison.Ordinal)), Is.True);
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("watch_region=GB", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task GetDiscoverAsyncMapsGenreIdsToGenreNames()
    {
        var handler = new PagedTrendingMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB",
            Language = "en-GB"
        });

        var results = await client.GetDiscoverAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 1));

        Assert.That(results[0].Genres, Does.Contain("Action"));
        Assert.That(results[0].Genres, Does.Contain("Science Fiction"));
    }

    [Test]
    public async Task GetDiscoverAsyncIncludesGenreIdWhenGenreFilterIsSet()
    {
        var handler = new PagedTrendingMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB",
            Language = "en-GB"
        });

        await client.GetDiscoverAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 25, GenreFilter: "Horror"));

        Assert.That(handler.RequestUris.Any(uri => uri.Contains("with_genres=27", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task GetDiscoverAsyncDoesNotSendInvalidRangeCertificationFilters()
    {
        var handler = new PagedTrendingMessageHandler();
        var client = new TmdbMetadataClient(new HttpClient(handler), new TmdbOptions
        {
            AccessToken = "private-access-token",
            RegionCode = "GB",
            Language = "en-GB"
        });

        await client.GetDiscoverAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 25, AgeRatingFilter: "15+"));

        Assert.That(handler.RequestUris.Any(uri => uri.Contains("certification=", StringComparison.Ordinal)), Is.False);
        Assert.That(handler.RequestUris.Any(uri => uri.Contains("certification_country=", StringComparison.Ordinal)), Is.False);
    }

    private sealed class CapturingMessageHandler : HttpMessageHandler
    {
        public string LastAuthorizationScheme { get; private set; }

        public string LastAuthorizationParameter { get; private set; }

        public string LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastRequestUri = request.RequestUri?.ToString();

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "results": [
                            {
                              "id": 348,
                              "title": "Alien",
                              "original_title": "Alien",
                              "overview": "A deep-space horror classic.",
                              "release_date": "1979-05-25",
                              "original_language": "en",
                              "vote_average": 8.5
                            }
                          ]
                        }
                        """)
                });
        }
    }

    private sealed class DetailsMessageHandler : HttpMessageHandler
    {
        public string LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "id": 5548,
                          "title": "RoboCop",
                          "original_title": "RoboCop",
                          "overview": "Part man. Part machine. All cop.",
                          "release_date": "1987-07-17",
                          "poster_path": "/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg",
                          "original_language": "en",
                          "runtime": 102,
                          "vote_average": 7.4,
                          "external_ids": {
                            "imdb_id": "tt0093870"
                          },
                          "release_dates": {
                            "results": [
                              {
                                "iso_3166_1": "GB",
                                "release_dates": [
                                  { "certification": "R" }
                                ]
                              }
                            ]
                          },
                          "credits": {
                            "crew": [
                              { "job": "Director", "name": "Paul Verhoeven" },
                              { "job": "Writer", "name": "Edward Neumeier" }
                            ]
                          },
                          "genres": [
                            { "id": 28, "name": "Action" },
                            { "id": 878, "name": "Science Fiction" }
                          ]
                        }
                        """)
                });
        }
    }

    private sealed class FindThenDetailsMessageHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri?.ToString() ?? string.Empty;
            RequestUris.Add(requestUri);

            if (requestUri.Contains("/3/find/", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {
                              "movie_results": [
                                {
                                  "id": 5548,
                                  "title": "RoboCop",
                                  "original_title": "RoboCop",
                                  "overview": "Lightweight find result.",
                                  "release_date": "1987-07-17",
                                  "poster_path": "/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg",
                                  "original_language": "en"
                                }
                              ]
                            }
                            """)
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "id": 5548,
                          "title": "RoboCop",
                          "original_title": "RoboCop",
                          "overview": "Part man. Part machine. All cop.",
                          "release_date": "1987-07-17",
                          "poster_path": "/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg",
                          "original_language": "en",
                          "runtime": 102,
                          "vote_average": 7.4,
                          "external_ids": {
                            "imdb_id": "tt0093870"
                          },
                          "release_dates": {
                            "results": [
                              {
                                "iso_3166_1": "GB",
                                "release_dates": [
                                  { "certification": "18" }
                                ]
                              }
                            ]
                          },
                          "credits": {
                            "crew": [
                              { "job": "Director", "name": "Paul Verhoeven" }
                            ]
                          },
                          "genres": [
                            { "id": 28, "name": "Action" },
                            { "id": 878, "name": "Science Fiction" }
                          ]
                        }
                        """)
                });
        }
    }

    private sealed class DetailsWithoutCertificationMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "id": 214198,
                          "title": "Rana: The Legend of Shadow Lake",
                          "original_title": "Rana: The Legend of Shadow Lake",
                          "overview": "Something stirs in the lake.",
                          "release_date": "1981-01-01",
                          "poster_path": "/poster-rana.jpg",
                          "original_language": "en",
                          "runtime": 89,
                          "vote_average": 5.3,
                          "release_dates": {
                            "results": [
                              {
                                "iso_3166_1": "GB",
                                "release_dates": [
                                  { "certification": "" }
                                ]
                              }
                            ]
                          },
                          "credits": {
                            "crew": [
                              { "job": "Director", "name": "Bill Rebane" }
                            ]
                          },
                          "genres": [
                            { "id": 27, "name": "Horror" }
                          ]
                        }
                        """)
                });
    }

    private sealed class PagedTrendingMessageHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUri = request.RequestUri?.ToString() ?? string.Empty;
            RequestUris.Add(requestUri);

            var page = requestUri.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            var startId = page == 1 ? 1 : 21;
            var results = Enumerable.Range(startId, 20)
                .Select(index =>
                    $$"""
                      {
                        "id": {{index}},
                        "title": "Movie {{index}}",
                        "original_title": "Movie {{index}}",
                        "overview": "Overview {{index}}",
                        "release_date": "1987-07-17",
                        "poster_path": "/poster-{{index}}.jpg",
                        "genre_ids": [28, 878],
                        "original_language": "en",
                        "vote_average": 7.0
                      }
                      """);

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                          {
                            "results": [
                              {{string.Join("," + Environment.NewLine, results)}}
                            ]
                          }
                          """)
                });
        }
    }
}
