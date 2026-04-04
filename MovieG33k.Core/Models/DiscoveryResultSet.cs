// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;

namespace MovieG33k.Core.Models;

/// <summary>
/// Holds the results of a discovery request.
/// </summary>
/// <remarks>
/// The UI can bind to this directly while the underlying services stay free to evolve their implementation details.
/// </remarks>
public sealed record DiscoveryResultSet(
    DiscoveryQuery Query,
    IReadOnlyList<LibraryItemSnapshot> Items,
    string StatusText);
