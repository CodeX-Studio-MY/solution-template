using System.Text;
using System.Text.Json;

namespace Acme.Generator.Emitters;

/// <summary>
/// .github/workflows/*.yml: one workflow per backend service and per frontend module, each with path filters, generated from
/// platform.yaml so adding a module adds its workflow. Each runs build (warnings are errors, analyzers on), tests, the architecture
/// tests, and the drift check (which covers the generated API clients). The shared security, container and template workflows are
/// generated here too, so every action is pinned in exactly one place.
/// </summary>
internal static class CiEmitter
{
    // Actions are pinned by commit SHA (the tag is in the comment). Dependabot proposes updates.
    private const string Checkout = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1";
    private const string SetupDotnet = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0";
    private const string SetupNode = "actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0";
    private const string SetupPnpm = "pnpm/action-setup@ea17c68df8912ef543352723c149a84f56e3d413 # v6.1.0";
    private const string UploadArtifact = "actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2";
    private const string CodeQlInit = "github/codeql-action/init@24c54180a607b1449ed407dd24f251e4e9147c8d # v4";
    private const string CodeQlAnalyze = "github/codeql-action/analyze@24c54180a607b1449ed407dd24f251e4e9147c8d # v4";
    private const string SetupBuildx = "docker/setup-buildx-action@f87e5991a6d7451dcb8d9637bfbc97413f497069 # v4.4.1";
    private const string BuildPush = "docker/build-push-action@c3c9e263c25d99ce0380d002d59b67737d91b0dc # v7.4.0";
    private const string DockerLogin = "docker/login-action@dbcb813823bdd20940b903addbd779551569679f # v4.6.0";
    private const string Trivy = "aquasecurity/trivy-action@ed142fd0673e97e23eac54620cfb913e5ce36c25 # v0.36.0";
    private const string Sbom = "anchore/sbom-action@66cbf4bc1f1c0d2edc94016e65bc221b6bb0ad6c # v0.24.3";
    private const string AttestProvenance = "actions/attest-build-provenance@4d101475d8b20a2381f78447822ac1eab6504dd8 # v4.2.2";
    private const string AttestSbom = "actions/attest-sbom@c604332985a26aa8cf1bdc465b92731239ec6b9e # v4.1.0";
    private const string Gitleaks = "gitleaks/gitleaks-action@e0c47f4f8be36e29cdc102c57e68cb5cbf0e8d1e # v3.0.0";

    /// <summary>Platform infrastructure that is not a service in platform.yaml but ships as an image and has its own workflow.</summary>
    private static readonly (string Key, string Pascal, string Project, string Assembly)[] Infrastructure =
    [
        ("gateway", "Gateway", "backend/src/Gateway/Gateway.csproj", "Gateway"),
        ("scheduler", "Scheduler", "backend/src/Scheduler/Scheduler.csproj", "Scheduler"),
        ("migrator", "Migrator", "backend/src/Migrator/Migrator.csproj", "Migrator"),
    ];

    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var product = platform.Product;
        var solution = $"backend/{product}.slnx";
        var aspireVersion = AspireVersion(files.RepoRoot);

        foreach (var service in platform.Services)
        {
            var paths = new[]
            {
                $"backend/src/Modules/{service.Pascal}/**",
                $"backend/src/Modules/{service.Pascal}.Migrations.*/**",
                $"backend/src/Hosts/{service.Pascal}.Host/**",
                $"backend/tests/{service.Pascal}.Tests/**",
            };
            files.Add($".github/workflows/service-{service.Key}.yml", "#", ServiceWorkflow($"Service {service.Key}", service.Key, paths, solution));
        }
        foreach (var (key, pascal, _, _) in Infrastructure)
            files.Add($".github/workflows/service-{key}.yml", "#", ServiceWorkflow($"Service {key}", key, [$"backend/src/{pascal}/**"], solution));

        foreach (var module in platform.Modules.Where(m => Directory.Exists(Path.Combine(files.RepoRoot, "frontend", "modules", m.Key))))
            files.Add($".github/workflows/frontend-{module.Key}.yml", "#", FrontendModuleWorkflow(module.Key, product));
        files.Add(".github/workflows/frontend-shell.yml", "#", FrontendShellWorkflow(product));
        files.Add(".github/workflows/frontend-packages.yml", "#", FrontendPackagesWorkflow());

        files.Add(".github/workflows/backend-core.yml", "#", BackendCoreWorkflow(solution));
        files.Add(".github/workflows/generated-files.yml", "#", GeneratedFilesWorkflow(aspireVersion));
        files.Add(".github/workflows/security.yml", "#", SecurityWorkflow(solution));
        files.Add(".github/workflows/containers.yml", "#", ContainersWorkflow(platform));
        files.Add(".github/workflows/templates.yml", "#", TemplatesWorkflow(product));
        files.Add(".github/dependabot.yml", "#", Dependabot());
    }

    private static string Header(string name, IEnumerable<string> paths, string selfFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"name: {name}");
        sb.AppendLine("on:");
        sb.AppendLine("  push:");
        sb.AppendLine("    branches: [main]");
        sb.AppendLine("    paths:");
        foreach (var path in paths.Append(selfFile))
            sb.AppendLine($"      - \"{path}\"");
        sb.AppendLine("  pull_request:");
        sb.AppendLine("    paths:");
        foreach (var path in paths.Append(selfFile))
            sb.AppendLine($"      - \"{path}\"");
        sb.AppendLine("  workflow_dispatch:");
        sb.AppendLine();
        sb.AppendLine("concurrency:");
        sb.AppendLine("  group: ${{ github.workflow }}-${{ github.ref }}");
        sb.AppendLine("  cancel-in-progress: true");
        sb.AppendLine();
        sb.AppendLine("permissions:");
        sb.AppendLine("  contents: read");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string DotnetSetup => $"""
              - uses: {SetupDotnet}
                with:
                  global-json-file: backend/global.json
              - run: dotnet tool restore
                working-directory: backend
        """;

    private static string NodeSetup => $"""
              - uses: {SetupPnpm}
                with:
                  package_json_file: frontend/package.json
              - uses: {SetupNode}
                with:
                  node-version: 24
                  cache: pnpm
                  cache-dependency-path: frontend/pnpm-lock.yaml
              - run: pnpm install --frozen-lockfile
                working-directory: frontend
        """;

    /// <summary>Common backend paths that make every service workflow stricter than its own folder: the shared build settings.</summary>
    private static readonly string[] SharedBackend =
    [
        "platform.yaml",
        "backend/Directory.*.props",
        "backend/global.json",
        "backend/BannedSymbols.txt",
    ];

    private static string ServiceWorkflow(string name, string key, string[] paths, string solution)
    {
        var self = $".github/workflows/service-{key}.yml";
        return Header(name, [.. paths, .. SharedBackend], self) + $"""
        jobs:
          build-test:
            runs-on: ubuntu-latest
            timeout-minutes: 45
            steps:
              - uses: {Checkout}
        {DotnetSetup}
              - run: dotnet restore {solution}
              # Warnings are errors (analyzers, nullable, banned APIs): a warning fails the build.
              - run: dotnet build {solution} --no-restore --configuration Release
              - name: Architecture tests
                run: dotnet test --project backend/tests/Architecture.Tests --no-build --configuration Release
              # Testcontainers starts SQL Server 2025, PostgreSQL 18, RabbitMQ and Valkey; both providers run in every test.
              - name: Tests of this service
                run: dotnet test --solution {solution} --no-build --configuration Release --filter-trait "service={key}" --ignore-exit-code 8
        {NodeSetup}
              - name: Generated files are up to date (including the API clients)
                run: scripts/generate.sh --check --skip-compose
        """;
    }

    private static string FrontendModuleWorkflow(string key, string product)
    {
        var self = $".github/workflows/frontend-{key}.yml";
        return Header($"Frontend module {key}", [$"frontend/modules/{key}/**", "frontend/packages/**", "frontend/tooling/**", "frontend/pnpm-lock.yaml", "frontend/pnpm-workspace.yaml", "platform.yaml"], self) + $"""
        jobs:
          build-test:
            runs-on: ubuntu-latest
            timeout-minutes: 20
            steps:
              - uses: {Checkout}
        {NodeSetup}
              - name: Typecheck, lint (boundary rules), unit tests and build, for this module and what it depends on
                run: pnpm exec turbo run typecheck lint test build --filter=@{product}/mfe-{key}...
                working-directory: frontend
              - run: pnpm exec prettier --check modules/{key}
                working-directory: frontend
        {DotnetSetup}
              - name: Generated files are up to date (including the API clients)
                run: scripts/generate.sh --check --skip-compose
        """;
    }

    private static string FrontendShellWorkflow(string product) =>
        Header("Frontend shell and end-to-end tests", ["frontend/apps/shell/**", "frontend/modules/**", "frontend/packages/**", "frontend/e2e/**", "frontend/tooling/**", "frontend/pnpm-lock.yaml"], ".github/workflows/frontend-shell.yml") + $"""
        jobs:
          build-test:
            runs-on: ubuntu-latest
            timeout-minutes: 30
            steps:
              - uses: {Checkout}
        {NodeSetup}
              - run: pnpm exec turbo run typecheck lint test --filter=@{product}/shell...
                working-directory: frontend
              - run: pnpm build
                working-directory: frontend
              - run: pnpm exec playwright install --with-deps chromium
                working-directory: frontend/e2e
              # The built shell and modules, served with the Gateway's CSP, with the API mocked: composition, roles, error isolation, axe.
              - run: pnpm e2e
                working-directory: frontend
              - uses: {UploadArtifact}
                if: failure()
                with:
                  name: playwright-report
                  path: frontend/e2e/playwright-report
        {DotnetSetup}
              - name: Generated files are up to date
                run: scripts/generate.sh --check --skip-compose
        """;

    private static string FrontendPackagesWorkflow() =>
        Header("Frontend packages", ["frontend/packages/**", "frontend/tooling/**", "frontend/pnpm-lock.yaml", "frontend/pnpm-workspace.yaml", "frontend/package.json", "frontend/eslint.config.js"], ".github/workflows/frontend-packages.yml") + $"""
        jobs:
          build-test:
            runs-on: ubuntu-latest
            timeout-minutes: 20
            steps:
              - uses: {Checkout}
        {NodeSetup}
              # ui-kit, web-core and api-clients are used by every module, so a change checks the whole workspace.
              - run: pnpm run typecheck && pnpm run lint && pnpm run test && pnpm format:check
                working-directory: frontend
        """;

    private static string BackendCoreWorkflow(string solution) =>
        Header("Backend core", ["backend/src/Core/**", "backend/src/Core.*/**", "backend/src/Contracts/**", "backend/tools/**", "backend/tests/**", "backend/Directory.*", "backend/global.json", "platform.yaml"], ".github/workflows/backend-core.yml") + $"""
        jobs:
          build-test:
            runs-on: ubuntu-latest
            timeout-minutes: 60
            steps:
              - uses: {Checkout}
        {DotnetSetup}
              - run: dotnet restore {solution}
              - run: dotnet build {solution} --no-restore --configuration Release
              # Everything, on both database providers.
              - run: dotnet test --solution {solution} --no-build --configuration Release
        """;

    private static string GeneratedFilesWorkflow(string aspireVersion) =>
        Header("Generated files", ["platform.yaml", "backend/**", "frontend/**", "deploy/**", "docs/**", "README.md"], ".github/workflows/generated-files.yml") + $"""
        jobs:
          drift:
            runs-on: ubuntu-latest
            timeout-minutes: 30
            steps:
              - uses: {Checkout}
        {DotnetSetup}
        {NodeSetup}
              # `aspire publish` produces deploy/docker-compose.yml from the AppHost.
              - run: dotnet tool install --global Aspire.Cli --version {aspireVersion}
              # Everything derived from platform.yaml, the configuration schema, the backend and the AppHost must be committed.
              - run: scripts/generate.sh --check
        """;

    private static string SecurityWorkflow(string solution) => $$$"""
        name: Security
        on:
          push:
            branches: [main]
          pull_request:
          schedule:
            - cron: "23 4 * * 1"
          workflow_dispatch:

        concurrency:
          group: ${{ github.workflow }}-${{ github.ref }}
          cancel-in-progress: true

        permissions:
          contents: read

        jobs:
          codeql:
            runs-on: ubuntu-latest
            permissions:
              contents: read
              security-events: write
            strategy:
              fail-fast: false
              matrix:
                language: [csharp, javascript-typescript]
            steps:
              - uses: {{{Checkout}}}
              - uses: {{{SetupDotnet}}}
                if: matrix.language == 'csharp'
                with:
                  global-json-file: backend/global.json
              - uses: {{{CodeQlInit}}}
                with:
                  languages: ${{ matrix.language }}
                  build-mode: ${{ matrix.language == 'csharp' && 'manual' || 'none' }}
              - if: matrix.language == 'csharp'
                run: dotnet build {{{solution}}} --configuration Release
              - uses: {{{CodeQlAnalyze}}}
                with:
                  category: "/language:${{ matrix.language }}"

          vulnerable-packages:
            runs-on: ubuntu-latest
            steps:
              - uses: {{{Checkout}}}
              - uses: {{{SetupDotnet}}}
                with:
                  global-json-file: backend/global.json
              - name: NuGet packages with known vulnerabilities (direct and transitive)
                run: |
                  dotnet restore {{{solution}}}
                  dotnet list {{{solution}}} package --vulnerable --include-transitive 2>&1 | tee vulnerabilities.txt
                  if grep -q "has the following vulnerable packages" vulnerabilities.txt; then exit 1; fi
              - uses: {{{SetupPnpm}}}
                with:
                  package_json_file: frontend/package.json
              - uses: {{{SetupNode}}}
                with:
                  node-version: 24
              - name: npm packages with known vulnerabilities
                run: pnpm audit --audit-level high
                working-directory: frontend

          licenses:
            runs-on: ubuntu-latest
            steps:
              - uses: {{{Checkout}}}
              - uses: {{{SetupDotnet}}}
                with:
                  global-json-file: backend/global.json
              - uses: {{{SetupPnpm}}}
                with:
                  package_json_file: frontend/package.json
              - uses: {{{SetupNode}}}
                with:
                  node-version: 24
              - run: pnpm install --frozen-lockfile
                working-directory: frontend
              # The license policy (SAAS_TEMPLATE_PROMPT 1): no library that needs a paid commercial license without a recorded decision.
              - run: scripts/check-licenses.sh

          secrets:
            runs-on: ubuntu-latest
            steps:
              - uses: {{{Checkout}}}
                with:
                  fetch-depth: 0
              - uses: {{{Gitleaks}}}
                env:
                  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        """ + "\n";

    private static string ContainersWorkflow(PlatformDefinition platform)
    {
        var matrix = new StringBuilder();
        foreach (var service in platform.Services)
            matrix.AppendLine($"          - {{ key: {service.Key}, file: deploy/docker/service.Dockerfile, project: backend/src/Hosts/{service.Pascal}.Host/{service.Pascal}.Host.csproj, assembly: {platform.ProductPascal}.{service.Pascal}.Host.dll }}");
        foreach (var (key, _, project, assembly) in Infrastructure.Where(i => i.Key != "gateway"))
            matrix.AppendLine($"          - {{ key: {key}, file: deploy/docker/service.Dockerfile, project: {project}, assembly: {platform.ProductPascal}.{assembly}.dll }}");
        matrix.AppendLine("          - { key: gateway, file: deploy/docker/gateway.Dockerfile, project: backend/src/Gateway/Gateway.csproj, assembly: '' }");

        return $$$"""
            name: Containers
            on:
              push:
                branches: [main]
                paths: ["backend/**", "frontend/**", "deploy/docker/**", "platform.yaml"]
              pull_request:
                paths: ["backend/**", "frontend/**", "deploy/docker/**", "platform.yaml"]
              workflow_dispatch:

            concurrency:
              group: ${{ github.workflow }}-${{ github.ref }}
              cancel-in-progress: true

            permissions:
              contents: read

            env:
              REGISTRY: ghcr.io
              PREFIX: ghcr.io/${{ github.repository_owner }}/{{{platform.Product}}}

            jobs:
              image:
                runs-on: ubuntu-latest
                timeout-minutes: 45
                permissions:
                  contents: read
                  packages: write        # push to GHCR (main only)
                  id-token: write        # signed build provenance
                  attestations: write
                strategy:
                  fail-fast: false
                  matrix:
                    include:
            {{{matrix}}}
                steps:
                  - uses: {{{Checkout}}}
                  - uses: {{{SetupBuildx}}}
                  - uses: {{{DockerLogin}}}
                    if: github.ref == 'refs/heads/main'
                    with:
                      registry: ${{ env.REGISTRY }}
                      username: ${{ github.actor }}
                      password: ${{ secrets.GITHUB_TOKEN }}
                  - name: Build (and push on main)
                    id: build
                    uses: {{{BuildPush}}}
                    with:
                      context: .
                      file: ${{ matrix.file }}
                      build-args: |
                        PROJECT=${{ matrix.project }}
                        ASSEMBLY=${{ matrix.assembly }}
                      tags: ${{ env.PREFIX }}-${{ matrix.key }}:${{ github.sha }}
                      load: ${{ github.ref != 'refs/heads/main' }}
                      push: ${{ github.ref == 'refs/heads/main' }}
                      provenance: false
                  - name: Scan the image for vulnerabilities (fails on high and critical findings)
                    uses: {{{Trivy}}}
                    with:
                      image-ref: ${{ env.PREFIX }}-${{ matrix.key }}:${{ github.sha }}
                      severity: HIGH,CRITICAL
                      ignore-unfixed: true
                      exit-code: "1"
                  - name: SBOM (CycloneDX)
                    uses: {{{Sbom}}}
                    with:
                      image: ${{ env.PREFIX }}-${{ matrix.key }}:${{ github.sha }}
                      format: cyclonedx-json
                      output-file: sbom-${{ matrix.key }}.cdx.json
                  - uses: {{{UploadArtifact}}}
                    with:
                      name: sbom-${{ matrix.key }}
                      path: sbom-${{ matrix.key }}.cdx.json
                  - name: Signed build provenance
                    if: github.ref == 'refs/heads/main'
                    uses: {{{AttestProvenance}}}
                    with:
                      subject-name: ${{ env.PREFIX }}-${{ matrix.key }}
                      subject-digest: ${{ steps.build.outputs.digest }}
                      push-to-registry: true
                  - name: Attach the SBOM to the image
                    if: github.ref == 'refs/heads/main'
                    uses: {{{AttestSbom}}}
                    with:
                      subject-name: ${{ env.PREFIX }}-${{ matrix.key }}
                      subject-digest: ${{ steps.build.outputs.digest }}
                      sbom-path: sbom-${{ matrix.key }}.cdx.json
                      push-to-registry: true
            """ + "\n";
    }

    private static string TemplatesWorkflow(string product) => $$$"""
        name: Templates
        on:
          push:
            branches: [main]
            paths: ["templates/**", "backend/tools/**", "backend/src/Core/**", "backend/tests/TestSupport/**", "frontend/packages/**", "scripts/new-module.*"]
          pull_request:
            paths: ["templates/**", "backend/tools/**", "backend/src/Core/**", "backend/tests/TestSupport/**", "frontend/packages/**", "scripts/new-module.*"]
          workflow_dispatch:

        concurrency:
          group: ${{ github.workflow }}-${{ github.ref }}
          cancel-in-progress: true

        permissions:
          contents: read

        jobs:
          instantiate:
            runs-on: ubuntu-latest
            timeout-minutes: 60
            steps:
              - uses: {{{Checkout}}}
        {{{DotnetSetup}}}
        {{{NodeSetup}}}
              # The templates are used to create a module with scripts/new-module; here that module is built and tested in full, so
              # the templates can never rot. No file is edited by hand.
              - run: dotnet tool install --global Aspire.Cli --version {{{AspireVersion(Directory.GetCurrentDirectory())}}}
              - run: scripts/new-module.sh ci-sample --name "CI Sample"
              - run: dotnet build backend/{{{product}}}.slnx --configuration Release
              - run: dotnet test --project backend/tests/CiSample.Tests --no-build --configuration Release
              - run: dotnet test --project backend/tests/Architecture.Tests --no-build --configuration Release
              - run: pnpm exec turbo run typecheck lint test build --filter=@{{{product}}}/mfe-ci-sample...
                working-directory: frontend
              - run: pnpm exec prettier --check modules/ci-sample
                working-directory: frontend
        """ + "\n";

    private static string Dependabot() => """
        version: 2
        updates:
          - package-ecosystem: nuget
            directory: /backend
            schedule: { interval: weekly }
            groups:
              microsoft: { patterns: ["Microsoft.*", "System.*"] }
              testing: { patterns: ["xunit*", "Shouldly", "NSubstitute", "Testcontainers*"] }
          - package-ecosystem: npm
            directory: /frontend
            schedule: { interval: weekly }
            groups:
              mui: { patterns: ["@mui/*", "@emotion/*"] }
              module-federation: { patterns: ["@module-federation/*", "@rsbuild/*"] }
              tooling: { patterns: ["eslint*", "typescript*", "prettier", "vitest", "@vitest/*"] }
          - package-ecosystem: github-actions
            directory: /
            schedule: { interval: weekly }
          - package-ecosystem: docker
            directory: /deploy/docker
            schedule: { interval: weekly }
        """;

    private static string AspireVersion(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "backend", "global.json");
        if (!File.Exists(path))
            path = Path.Combine(repoRoot, "..", "backend", "global.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("msbuild-sdks").GetProperty("Aspire.AppHost.Sdk").GetString()!;
    }
}
