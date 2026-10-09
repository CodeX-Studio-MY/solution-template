namespace Acme.Generator.Emitters;

/// <summary>backend/Platform.g.props: product name and org for assembly names, namespaces and package metadata.</summary>
internal static class MsBuildEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files) =>
        files.AddXml("backend/Platform.g.props", $"""
            <Project>
              <PropertyGroup>
                <PlatformProduct>{platform.Product}</PlatformProduct>
                <PlatformProductPascal>{platform.ProductPascal}</PlatformProductPascal>
                <PlatformOrg>{platform.Org}</PlatformOrg>
              </PropertyGroup>
            </Project>
            """);
}
