namespace Farm360.Core.Platform;

/// <summary>An access module declared in <c>platform.yaml</c>: something roles and plans apply to.</summary>
/// <param name="Key">The module key used in tokens, routes and code.</param>
/// <param name="DisplayName">The default display name.</param>
/// <param name="Id">The stable primary key of the module in the catalog.</param>
/// <param name="Plans">The plans that include this module.</param>
public sealed record ModuleInfo(string Key, string DisplayName, Guid Id, IReadOnlyList<string> Plans);
