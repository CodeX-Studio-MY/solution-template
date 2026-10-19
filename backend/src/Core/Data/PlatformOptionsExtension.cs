using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Saaskit.Core.Data;

/// <summary>Carries the provider and schema into a <see cref="PlatformDbContext"/>.</summary>
internal sealed class PlatformOptionsExtension(IDatabaseProvider provider, string schema) : IDbContextOptionsExtension
{
    public IDatabaseProvider Provider { get; } = provider;

    public string Schema { get; } = schema;

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(PlatformOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new PlatformOptionsExtension Extension => (PlatformOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"PlatformProvider={Extension.Provider.Name} Schema={Extension.Schema} ";

        public override int GetServiceProviderHashCode() => HashCode.Combine(Extension.Provider.Name, Extension.Schema);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info
            && info.Extension.Provider.Name == Extension.Provider.Name
            && info.Extension.Schema == Extension.Schema;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Platform:Provider"] = Extension.Provider.Name;
            debugInfo["Platform:Schema"] = Extension.Schema;
        }
    }
}

/// <summary>Caches one EF Core model per context type and provider.</summary>
internal sealed class PlatformModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(Microsoft.EntityFrameworkCore.DbContext context, bool designTime) =>
        context is PlatformDbContext platform
            ? (context.GetType(), platform.Provider.Name, designTime)
            : (context.GetType(), designTime);
}
