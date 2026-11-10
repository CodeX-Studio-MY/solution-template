namespace Saaskit.Contracts.Sample;

/// <summary>
/// The shapes this module returns, shared with the modules that call it through <see cref="ISampleClient"/>. The module's own API
/// returns them too, so the OpenAPI document and the frontend's typed client use the same records. Keep them to what another module
/// may rely on: add optional fields, never rename or remove one. Replace the example with your own.
/// </summary>
/// <param name="Id">The item.</param>
/// <param name="Name">What it is called.</param>
/// <param name="ETag">Its version, for <c>If-Match</c> on a change.</param>
public sealed record ItemView(Guid Id, string Name, string ETag);
