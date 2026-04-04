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
/// Represents a streaming or purchase provider associated with a title.
/// </summary>
/// <remarks>
/// Provider availability is intentionally modelled now so later TMDb watch-provider enrichment has a natural home.
/// </remarks>
public sealed record Provider(int ProviderId, string Name, string LogoPath = null);
