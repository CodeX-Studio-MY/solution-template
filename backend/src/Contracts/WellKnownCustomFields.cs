namespace Saaskit.Contracts;

/// <summary>
/// Custom field keys that modules agree on, so one module's data can feed another through events (customization ladder, rungs 4
/// and 5). Add one constant per key, for example <c>public const string CostCenter = "cost-center";</c>; <c>scripts/generate</c>
/// writes them to the frontend as <c>wellKnownCustomFields</c>. This is a platform contract: it lives here, not in a module's folder,
/// so removing a module never removes it (the API exporter reads it).
/// </summary>
public static class WellKnownCustomFields
{
}
