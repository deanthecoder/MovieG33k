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
using MovieG33k.ViewModels;

namespace MovieG33k.Tests;

public sealed class LibraryItemSnapshotViewModelTests
{
    [Test]
    public void PosterUrlUsesTmdbCdnForTmdbPosterPathsOnUnixStyleRoots()
    {
        var title = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "A cyborg lawman.",
            new DateOnly(1987, 7, 17),
            "/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg",
            null,
            ["Action"],
            "en");

        var viewModel = new LibraryItemSnapshotViewModel(new LibraryItemSnapshot(title));

        Assert.That(viewModel.PosterUrl, Is.EqualTo("https://image.tmdb.org/t/p/w342/esmAU0fCO28FbS6bUBKLAzJrohZ.jpg"));
    }

    [Test]
    public void PosterUrlKeepsExistingLocalAbsoluteFilePaths()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var title = new MovieEntry(
                new TitleIdentifiers(11357, "tt0091059"),
                "Flight of the Navigator",
                "Flight of the Navigator",
                "A kid and a spaceship.",
                new DateOnly(1986, 8, 1),
                tempFile,
                null,
                ["Family"],
                "en");

            var viewModel = new LibraryItemSnapshotViewModel(new LibraryItemSnapshot(title));

            Assert.That(viewModel.PosterUrl, Is.EqualTo(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
