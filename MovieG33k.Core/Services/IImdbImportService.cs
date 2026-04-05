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

namespace MovieG33k.Core.Services;

/// <summary>
/// Describes the optional IMDb bootstrap import flow.
/// </summary>
/// <remarks>
/// Import stays behind an abstraction because it is a convenience path rather than the core operating model of the app.
/// </remarks>
public interface IImdbImportService
{
    /// <summary>
    /// Imports IMDb CSV data from disk and attempts to resolve rows to TMDb entries.
    /// </summary>
    Task<ImdbImportResult> ImportAsync(
        FileInfo csvFile,
        IProgress<ImdbImportProgress> progress = null,
        CancellationToken cancellationToken = default);
}
