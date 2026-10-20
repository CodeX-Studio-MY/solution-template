namespace Saaskit.Generator.Emitters;

/// <summary>
/// .github/workflows/ci.yml: one build-only workflow, so a fresh clone gets a green check with no secrets or org setup. The backend
/// builds with warnings as errors (analyzers, nullable, banned APIs); the frontend typechecks, lints and builds. Tests, the drift
/// check, security scans and image publishing are left to each product (docs/deviations.md).
/// </summary>
internal static class CiEmitter
{
    // Actions are pinned by commit SHA (the tag is in the comment).
    private const string Checkout = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1";
    private const string SetupDotnet = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0";
    private const string SetupNode = "actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0";
    private const string SetupPnpm = "pnpm/action-setup@ea17c68df8912ef543352723c149a84f56e3d413 # v6.1.0";

    public static void Emit(PlatformDefinition platform, GeneratedFiles files) =>
        files.Add(".github/workflows/ci.yml", "#", CiWorkflow($"backend/{platform.Product}.slnx"));

    private static string CiWorkflow(string solution) => $$$"""
        name: CI
        on:
          push:
            branches: [main]
          pull_request:
          workflow_dispatch:

        concurrency:
          group: ${{ github.workflow }}-${{ github.ref }}
          cancel-in-progress: ${{ github.event_name == 'pull_request' }}

        permissions:
          contents: read

        jobs:
          backend:
            runs-on: ubuntu-latest
            timeout-minutes: 20
            steps:
              - uses: {{{Checkout}}}
              - uses: {{{SetupDotnet}}}
                with:
                  global-json-file: global.json
              # Warnings are errors (analyzers, nullable, banned APIs): a warning fails the build.
              - run: dotnet build {{{solution}}} --configuration Release

          frontend:
            runs-on: ubuntu-latest
            timeout-minutes: 20
            steps:
              - uses: {{{Checkout}}}
              - uses: {{{SetupPnpm}}}
                with:
                  package_json_file: frontend/package.json
              - uses: {{{SetupNode}}}
                with:
                  node-version: 24
                  cache: pnpm
                  cache-dependency-path: frontend/pnpm-lock.yaml
              - run: pnpm install --frozen-lockfile
                working-directory: frontend
              - run: pnpm run typecheck && pnpm run lint && pnpm build
                working-directory: frontend
        """ + "\n";
}
