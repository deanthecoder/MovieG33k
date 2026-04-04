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
/// Stores how a title can be accessed through a provider in a specific region.
/// </summary>
/// <remarks>
/// TMDb watch-provider data can later populate this without changing the rest of the domain model.
/// </remarks>
public sealed record ProviderAvailability(
    TitleIdentifiers Identifiers,
    TitleKind Kind,
    string RegionCode,
    Provider Provider,
    string AccessModel,
    string DeepLink = null);
