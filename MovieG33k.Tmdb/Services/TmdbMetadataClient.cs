// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;
using MovieG33k.Tmdb.Models;

namespace MovieG33k.Tmdb.Services;

/// <summary>
/// Minimal TMDb client used for discovery and IMDb resolution.
/// </summary>
/// <remarks>
/// The client already supports live TMDb calls, but it also falls back to curated sample data so the first-run scaffold remains useful without credentials.
/// </remarks>
public sealed class TmdbMetadataClient : ITmdbMetadataClient
{
    private readonly HttpClient m_httpClient;
    private readonly TmdbOptions m_options;

    /// <summary>
    /// Creates a new TMDb metadata client.
    /// </summary>
    public TmdbMetadataClient(HttpClient httpClient, TmdbOptions options)
    {
        m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        m_options = options ?? throw new ArgumentNullException(nameof(options));
        if (m_httpClient.BaseAddress == null)
            m_httpClient.BaseAddress = new Uri(m_options.BaseUrl, UriKind.Absolute);
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(m_options.AccessToken) || !string.IsNullOrWhiteSpace(m_options.ApiKey);

    /// <inheritdoc />
    public string RegionCode => m_options.RegionCode;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Query))
            return await GetTrendingAsync(query.Kind, query.MaxResults, cancellationToken);

        if (!IsConfigured)
            return GetStubSearchResults(query.Kind, query.Query, query.MaxResults);

        var path = query.Kind == TitleKind.Movie ? "/3/search/movie" : "/3/search/tv";
        var requestUri = BuildRequestUri(path, new Dictionary<string, string>
        {
            ["query"] = query.Query,
            ["include_adult"] = "false",
            ["language"] = m_options.Language
        });

        return await SendAndMapAsync(requestUri, query.Kind, query.MaxResults, cancellationToken)
            ?? GetStubSearchResults(query.Kind, query.Query, query.MaxResults);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return GetStubTrendingResults(kind, maxResults);

        var mediaPath = kind == TitleKind.Movie ? "movie" : "tv";
        var requestUri = BuildRequestUri($"/3/trending/{mediaPath}/day", new Dictionary<string, string>
        {
            ["language"] = m_options.Language
        });

        return await SendAndMapAsync(requestUri, kind, maxResults, cancellationToken)
            ?? GetStubTrendingResults(kind, maxResults);
    }

    /// <inheritdoc />
    public async Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
            throw new ArgumentException("An IMDb identifier is required.", nameof(imdbId));

        if (!IsConfigured)
            return GetStubTrendingResults(kind, 10)
                .FirstOrDefault(title => string.Equals(title.Identifiers.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));

        var requestUri = BuildRequestUri($"/3/find/{imdbId}", new Dictionary<string, string>
        {
            ["external_source"] = "imdb_id",
            ["language"] = m_options.Language
        });

        try
        {
            using var request = CreateRequestMessage(requestUri);
            using var response = await m_httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            var resultProperty = kind == TitleKind.Movie ? "movie_results" : "tv_results";
            if (!document.RootElement.TryGetProperty(resultProperty, out var resultArray) || resultArray.GetArrayLength() == 0)
                return null;

            return MapTitle(resultArray[0], kind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return GetStubTrendingResults(kind, 10)
                .FirstOrDefault(title => string.Equals(title.Identifiers.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task<IReadOnlyList<CatalogTitle>> SendAndMapAsync(
        string requestUri,
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequestMessage(requestUri);
            using var response = await m_httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("results", out var resultsElement))
                return Array.Empty<CatalogTitle>();

            return resultsElement
                .EnumerateArray()
                .Select(element => MapTitle(element, kind))
                .Where(title => title != null)
                .Take(maxResults)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private string BuildRequestUri(string path, IReadOnlyDictionary<string, string> values)
    {
        var queryValues = new Dictionary<string, string>(values)
        {
            ["region"] = m_options.RegionCode
        };
        if (string.IsNullOrWhiteSpace(m_options.AccessToken))
            queryValues["api_key"] = m_options.ApiKey;

        var encodedValues = queryValues
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");

        return $"{path}?{string.Join("&", encodedValues)}";
    }

    private HttpRequestMessage CreateRequestMessage(string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(m_options.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m_options.AccessToken);

        return request;
    }

    private static CatalogTitle MapTitle(JsonElement element, TitleKind kind)
    {
        var identifiers = new TitleIdentifiers(
            element.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : null,
            null);
        var nameProperty = kind == TitleKind.Movie ? "title" : "name";
        var originalNameProperty = kind == TitleKind.Movie ? "original_title" : "original_name";
        var dateProperty = kind == TitleKind.Movie ? "release_date" : "first_air_date";
        DateOnly? releaseDate = element.TryGetProperty(dateProperty, out var dateElement) &&
                                DateOnly.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : null;

        var genres = Array.Empty<string>();
        if (kind == TitleKind.Movie)
        {
            return new MovieEntry(
                identifiers,
                element.TryGetProperty(nameProperty, out var nameElement) ? nameElement.GetString() : null,
                element.TryGetProperty(originalNameProperty, out var originalNameElement) ? originalNameElement.GetString() : null,
                element.TryGetProperty("overview", out var overviewElement) ? overviewElement.GetString() : null,
                releaseDate,
                element.TryGetProperty("poster_path", out var posterElement) ? posterElement.GetString() : null,
                element.TryGetProperty("backdrop_path", out var backdropElement) ? backdropElement.GetString() : null,
                genres,
                element.TryGetProperty("original_language", out var languageElement) ? languageElement.GetString() : null,
                PublicRating: TryGetPublicRating(element));
        }

        return new TvShowEntry(
            identifiers,
            element.TryGetProperty(nameProperty, out var tvNameElement) ? tvNameElement.GetString() : null,
            element.TryGetProperty(originalNameProperty, out var tvOriginalNameElement) ? tvOriginalNameElement.GetString() : null,
            element.TryGetProperty("overview", out var tvOverviewElement) ? tvOverviewElement.GetString() : null,
            releaseDate,
            element.TryGetProperty("poster_path", out var tvPosterElement) ? tvPosterElement.GetString() : null,
            element.TryGetProperty("backdrop_path", out var tvBackdropElement) ? tvBackdropElement.GetString() : null,
            genres,
            element.TryGetProperty("original_language", out var tvLanguageElement) ? tvLanguageElement.GetString() : null,
            PublicRating: TryGetPublicRating(element));
    }

    private static decimal? TryGetPublicRating(JsonElement element)
    {
        if (!element.TryGetProperty("vote_average", out var voteAverageElement))
            return null;

        return voteAverageElement.ValueKind switch
        {
            JsonValueKind.Number when voteAverageElement.TryGetDecimal(out var publicRating) => decimal.Round(publicRating, 1),
            JsonValueKind.Number when voteAverageElement.TryGetDouble(out var publicRatingDouble) => decimal.Round((decimal)publicRatingDouble, 1),
            _ => null
        };
    }

    private static IReadOnlyList<CatalogTitle> GetStubTrendingResults(TitleKind kind, int maxResults)
    {
        IReadOnlyList<CatalogTitle> items =
            kind == TitleKind.Movie
                ? new CatalogTitle[]
                {
                    new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "The crew of a commercial spacecraft answers a distress signal and discovers a perfect organism stalking them in the dark.", new DateOnly(1979, 5, 25), "/vfrQk5IPloGg1v9Rzbh2Eg3VGyM.jpg", "/AmR3JG1VQVxU8TfAvljUhfSFUOx.jpg", ["Horror", "Science Fiction"], "en", 117, 8.2m),
                    new MovieEntry(new TitleIdentifiers(679, "tt0090605"), "Aliens", "Aliens", "Ripley returns to face a colony overrun by the same terrifying species she barely survived the first time.", new DateOnly(1986, 7, 18), "/r1x5JGpyqZU8PYhbs4UcrO1Xb6x.jpg", "/d7px1FQxW4tngdACVRsCSaZq0Xl.jpg", ["Action", "Science Fiction"], "en", 137, 8.0m),
                    new MovieEntry(new TitleIdentifiers(603692, "tt15398776"), "Oppenheimer", "Oppenheimer", "A theoretical physicist leads the Manhattan Project and grapples with its consequences.", new DateOnly(2023, 7, 21), "/8Gxv9qnaD6bZ3fW7KvxJ9jE4t4T.jpg", "/fm6KqXpk3M2HVveHwCrBSSBaO0V.jpg", ["Drama", "History"], "en", 181, 8.1m),
                    new MovieEntry(new TitleIdentifiers(872585, "tt1517268"), "Barbie", "Barbie", "Barbie and Ken tumble out of a perfect plastic world into something messier and much more human.", new DateOnly(2023, 7, 21), "/iuFNMS8U5cb6xfzi51Dbkovj7vM.jpg", "/nHf61UzkfFno5X1ofIhugCPus2R.jpg", ["Comedy", "Fantasy"], "en", 114, 7.0m),
                    new MovieEntry(new TitleIdentifiers(346698, "tt4154796"), "Avengers: Endgame", "Avengers: Endgame", "The remaining Avengers mount a last stand to undo the damage wrought by Thanos.", new DateOnly(2019, 4, 26), "/or06FN3Dka5tukK1e9sl16pB3iy.jpg", "/7RyHsO4yDXtBv1zUU3mTpHeQ0d5.jpg", ["Action", "Science Fiction"], "en", 181, 8.3m)
                }
                : new CatalogTitle[]
                {
                    new TvShowEntry(new TitleIdentifiers(1396, "tt0903747"), "Breaking Bad", "Breaking Bad", "A chemistry teacher turned meth producer spirals into increasingly dangerous choices.", new DateOnly(2008, 1, 20), "/ztkUQFLlC19CCMYHW9o1zWhJRNq.jpg", "/tsRy63Mu5cu8etL1X7ZLyf7UP1M.jpg", ["Drama", "Crime"], "en", 5, 62, 8.9m),
                    new TvShowEntry(new TitleIdentifiers(94997, "tt2861424"), "House of the Dragon", "House of the Dragon", "The Targaryen dynasty tears itself apart in a brutal struggle for succession.", new DateOnly(2022, 8, 21), "/z2yahl2uefxDCl0nogcRBstwruJ.jpg", "/etj8E2o0Bud0HkONVQPjyCkIvpv.jpg", ["Fantasy", "Drama"], "en", 2, 18, 8.4m),
                    new TvShowEntry(new TitleIdentifiers(2316, "tt0411008"), "Lost", "Lost", "A plane crash strands survivors on an island that refuses to stay simple.", new DateOnly(2004, 9, 22), "/ogWw7A5XzeoT6kcK96mdNmQ4aiH.jpg", "/m8JTwHFwX7I7JY5fPe4SjqejWag.jpg", ["Adventure", "Mystery"], "en", 6, 121, 8.0m)
                };

        return items.Take(maxResults).ToArray();
    }

    private static IReadOnlyList<CatalogTitle> GetStubSearchResults(TitleKind kind, string query, int maxResults) =>
        GetStubTrendingResults(kind, maxResults * 2)
            .Where(title => title.Name?.Contains(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true ||
                            title.Overview?.Contains(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true)
            .Take(maxResults)
            .ToArray();
}
