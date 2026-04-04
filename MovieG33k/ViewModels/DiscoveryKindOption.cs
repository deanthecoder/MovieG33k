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

namespace MovieG33k.ViewModels;

/// <summary>
/// Represents a UI filter option for switching between movie and TV discovery.
/// </summary>
/// <remarks>
/// This keeps friendly display text out of the core enum while still enabling strongly typed bindings.
/// </remarks>
public sealed record DiscoveryKindOption(string DisplayName, TitleKind Kind);
