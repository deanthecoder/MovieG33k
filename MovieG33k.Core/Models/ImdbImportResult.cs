// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MovieG33k.Core.Models;

/// <summary>
/// Summarizes an IMDb import run.
/// </summary>
/// <remarks>
/// Returning both the imported rows and a status message keeps early import work easy to inspect in tests and UI.
/// </remarks>
public sealed record ImdbImportResult(
    IReadOnlyList<ImportedImdbItem> Items,
    int ResolvedItemCount,
    string StatusText);
