namespace Farm360.Core.Hosting;

/// <summary>Health check tags. <c>/health/live</c> runs no checks; <c>/health/ready</c> runs those tagged <see cref="Ready"/>.</summary>
public static class HealthTags
{
    public const string Ready = "ready";
}
