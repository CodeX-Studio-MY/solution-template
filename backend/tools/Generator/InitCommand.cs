using System.Text;
using System.Text.RegularExpressions;

namespace Farm360.Generator;

/// <summary>
/// <c>scripts/generate</c> on a fresh copy of the template (the <c>.template</c> marker is there): scaffolds the product. It asks for
/// (or takes as options) the product's name and defaults, writes them into platform.yaml, renames the template's placeholder product
/// everywhere, adds the first business modules, generates everything, installs, builds and checks, removes what only the template
/// needs, and commits. Run again after a failure, it continues where it stopped. Once it finishes, scripts/generate only regenerates.
/// </summary>
internal static partial class InitCommand
{
    /// <summary>
    /// The template's placeholder product key, which the codemod renames everywhere. Written in two halves so the codemod leaves this
    /// one alone: a resumed run must still tell a renamed product from the template.
    /// </summary>
    internal static readonly string TemplateProduct = string.Concat("saas", "kit");

    /// <summary>Present only in the template: its presence makes scripts/generate run this command.</summary>
    internal const string Marker = ".template";

    /// <summary>What only the template needs, removed from the product.</summary>
    private static readonly string[] TemplateOnly = [Marker, ".github/workflows/init.yml"];

    /// <summary>Parts of README.md about the template itself, between these lines, are removed from the product's README.</summary>
    internal const string TemplateSectionStart = "<!-- BEGIN TEMPLATE ONLY -->";

    internal const string TemplateSectionEnd = "<!-- END TEMPLATE ONLY -->";

    public static int Run(string repoRoot, string[] args)
    {
        if (!File.Exists(Path.Combine(repoRoot, Marker)))
            throw new PlatformDefinitionException("This solution is already scaffolded (there is no .template marker). Run scripts/generate to regenerate.");

        var options = InitOptions.Parse(args);
        var platformPath = Path.Combine(repoRoot, "platform.yaml");
        var current = PlatformDefinition.Load(platformPath);
        var resuming = current.Product != TemplateProduct;
        var answers = resuming ? InitAnswers.From(current, options) : InitAnswers.Ask(current, options);

        Preflight(options);

        // It changes this folder in place, so say which one, in case this is the template's own clone. Only --yes skips the question:
        // without an answer (no input, or input that isn't "y") nothing changes.
        if (!resuming && !options.Yes)
        {
            Console.WriteLine();
            Console.Write($"This turns {repoRoot} into '{answers.Product}' in place. Continue? [y/N]: ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.WriteLine("Nothing was changed. Make a copy of the template (\"Use this template\", or git clone into a new folder) and run this there.");
                return 1;
            }
        }
        Console.WriteLine();
        Console.WriteLine(resuming
            ? $"Continuing the scaffold of '{answers.Product}' ({Naming.Pascal(answers.Product)})."
            : $"Scaffolding '{answers.Product}' ({Naming.Pascal(answers.Product)}) from the template.");

        // 1. The product's facts, then the one-time rename. Both are skipped when a previous run got this far.
        if (!resuming)
        {
            WritePlatformYaml(platformPath, answers);
            PlatformDefinition.Load(platformPath);   // fails here, before anything else changes, if the answers don't validate
            var renamed = ProductRenamer.Run(repoRoot, TemplateProduct, answers.Product);
            Console.WriteLine($"Renamed '{TemplateProduct}' to '{answers.Product}' in {renamed.ChangedFiles.Count} files and {renamed.RenamedPaths.Count} paths.");
        }

        // 2. From here on the code on disk has the product's namespaces, and this process was built against the template's: every
        //    step that compiles runs as a new process (dotnet run builds the generator again).
        var pnpm = PnpmPath.Resolve();
        Step(pnpm, Path.Combine(repoRoot, "frontend"), "install");   // the package scope changed, so the lockfile does too
        Step("dotnet", Path.Combine(repoRoot, "backend"), "tool", "restore");   // dotnet ef, for the migrations
        var skipCompose = !AspireAvailable();
        if (skipCompose)
            Console.WriteLine("The Aspire CLI isn't installed: deploy/docker-compose.yml is left as it is. Install it (https://aspire.dev) and run scripts/generate later.");

        // A module an earlier run added to platform.yaml but stopped before its migrations is resumed, not added again.
        var existing = PlatformDefinition.Load(platformPath).Modules.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        string[] compose = skipCompose ? ["--skip-compose"] : [];
        foreach (var module in answers.Modules)
        {
            if (!existing.Contains(module))
                Generator(repoRoot, ["new-module", module, .. compose]);
            else if (!HasModuleMigrations(repoRoot, module))
                Generator(repoRoot, ["new-module", module, "--resume", .. compose]);
        }
        RecreateCatalogMigrations(repoRoot);
        Generator(repoRoot, ["generate", .. compose]);

        // 3. A first check that what was generated builds and follows the rules. The integration tests need Docker and take longer.
        if (!options.SkipChecks)
        {
            Step("dotnet", repoRoot, "build", $"backend/{answers.Product}.slnx");
            Step("dotnet", repoRoot, "test", "--project", "backend/tests/Architecture.Tests", "--no-build");
            // The build first: it also stages dist/, which the browser tests serve.
            Step(pnpm, Path.Combine(repoRoot, "frontend"), "build");
            Step(pnpm, Path.Combine(repoRoot, "frontend"), "exec", "turbo", "run", "typecheck", "lint", "test");
        }

        // 4. Nothing of the template is left, so scripts/generate only regenerates from now on.
        var readme = Path.Combine(repoRoot, "README.md");
        File.WriteAllText(readme, WithoutTemplateSections(File.ReadAllText(readme)), new UTF8Encoding(false));
        foreach (var path in TemplateOnly.Select(p => Path.Combine(repoRoot, p)))
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        if (!options.NoGit)
            Commit(repoRoot, answers.Product);

        Console.WriteLine();
        Console.WriteLine($"{Naming.Pascal(answers.Product)} is ready.");
        Console.WriteLine("  scripts/dev-up                       start everything locally and sign in to https://demo.localhost:7443/");
        Console.WriteLine("  scripts/new-module <key>             add a business module (backend, frontend, migrations, CI)");
        Console.WriteLine($"  dotnet test --solution backend/{answers.Product}.slnx   every test, including integration tests (Docker)");
        return 0;
    }

    /// <summary>Writes the answers over the template's values, line by line, so the file keeps its comments and layout.</summary>
    internal static void WritePlatformYaml(string path, InitAnswers answers)
    {
        var plans = $"[{string.Join(", ", answers.Plans)}]";
        var lines = File.ReadAllLines(path).Select(line => line switch
        {
            _ when line.StartsWith("product:", StringComparison.Ordinal) => $"product: {answers.Product}",
            _ when line.StartsWith("org:", StringComparison.Ordinal) => $"org: {answers.Org}",
            _ when line.StartsWith("cloud:", StringComparison.Ordinal) => $"cloud: {answers.Cloud}",
            _ when line.StartsWith("plans:", StringComparison.Ordinal) => ReplaceValue(line, "plans:", plans),
            _ when line.StartsWith("  timezone:", StringComparison.Ordinal) => $"  timezone: {answers.TimeZone}",
            _ when line.StartsWith("  currency:", StringComparison.Ordinal) => $"  currency: {answers.Currency}",
            _ when line.StartsWith("  locales:", StringComparison.Ordinal) => $"  locales: [{string.Join(", ", answers.Locales)}]",
            // The platform's own modules are in every plan.
            _ when line.StartsWith("  - {", StringComparison.Ordinal) && line.Contains("plans: [", StringComparison.Ordinal) => ModulePlans().Replace(line, $"plans: {plans}"),
            _ => line,
        });
        File.WriteAllText(path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    /// <summary>The text without its template-only sections (each from its start line to its end line, inclusive).</summary>
    internal static string WithoutTemplateSections(string text)
    {
        var kept = new List<string>();
        var inside = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimEnd('\r') == TemplateSectionStart)
                inside = true;
            else if (line.TrimEnd('\r') == TemplateSectionEnd)
                inside = false;
            else if (!inside)
                kept.Add(line);
        }
        return string.Join('\n', kept);
    }

    /// <summary>Replaces the value after <paramref name="key"/>, keeping a trailing comment.</summary>
    private static string ReplaceValue(string line, string key, string value)
    {
        var comment = line.IndexOf(" #", StringComparison.Ordinal);
        var tail = comment >= 0 ? line[comment..] : "";
        var head = $"{key} {value}";
        return comment >= 0 ? head.PadRight(comment) + tail : head;
    }

    private static void Preflight(InitOptions options)
    {
        foreach (var (tool, args) in new[] { ("dotnet", "--version"), ("node", "--version") })
            Processes.Capture(tool, Directory.GetCurrentDirectory(), $"{tool} is required.", args);
        Processes.Capture(PnpmPath.Resolve(), Directory.GetCurrentDirectory(), "pnpm is required: npm install -g pnpm", "--version");
        if (!options.NoGit)
            Processes.Capture("git", Directory.GetCurrentDirectory(), "git is required, or pass --no-git.", "--version");
    }

    private static bool AspireAvailable()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_CLI")))
            return true;
        try
        {
            Processes.Capture("aspire", Directory.GetCurrentDirectory(), "", "--version");
            return true;
        }
        catch (PlatformDefinitionException)
        {
            return false;
        }
    }

    /// <summary>
    /// The catalog seeds the modules and plans from platform.yaml, so the answers and the new modules change it. A new product has no
    /// history to keep: its catalog starts with one <c>Initial</c> migration per provider, made from the model as it now is.
    /// </summary>
    private static void RecreateCatalogMigrations(string repoRoot)
    {
        var backend = Path.Combine(repoRoot, "backend");
        foreach (var provider in DataContexts.Providers(PlatformDefinition.Load(Path.Combine(repoRoot, "platform.yaml"))))
        {
            var project = $"src/Core.Catalog.Migrations.{provider.Suffix}";
            var migrations = Path.Combine(backend, project, "Migrations", "Catalog");
            if (Directory.Exists(migrations))
                Directory.Delete(migrations, recursive: true);
            // `dotnet ef` is a local tool: it is only on the path inside backend/ (see backend/.config/dotnet-tools.json).
            Step("dotnet", backend, "restore", project);
            Step("dotnet", backend, "ef", "migrations", "add", "Initial", "-p", project, "-s", project, "-c", "CatalogDbContext", "-o", "Migrations/Catalog");
        }
    }

    /// <summary>Whether the module's first migration exists for every database provider.</summary>
    private static bool HasModuleMigrations(string repoRoot, string module)
    {
        var pascal = Naming.Pascal(module);
        var platform = PlatformDefinition.Load(Path.Combine(repoRoot, "platform.yaml"));
        return DataContexts.Providers(platform).All(provider => NewModuleCommand.HasMigration(
            Path.Combine(repoRoot, "backend", "src", "Modules", $"{pascal}.Migrations.{provider.Suffix}", "Migrations", pascal), "Initial"));
    }

    /// <summary>Runs the generator as a new process, so it is built from the code as it now is on disk.</summary>
    private static void Generator(string repoRoot, string[] arguments) =>
        Step("dotnet", repoRoot, ["run", "--project", "backend/tools/Generator", "--", .. arguments]);

    private static void Commit(string repoRoot, string product)
    {
        if (!Directory.Exists(Path.Combine(repoRoot, ".git")))
            Step("git", repoRoot, "init", "--initial-branch=main");
        Step("git", repoRoot, "add", "--all");
        Step("git", repoRoot, "commit", "--quiet", "--message", $"Scaffold {Naming.Pascal(product)} from the solution template");
    }

    private static void Step(string fileName, string workingDirectory, params string[] arguments) =>
        Processes.Run(fileName, workingDirectory, "Fix the problem and run scripts/generate again: it continues where it stopped.", arguments);

    [GeneratedRegex(@"plans: \[[^\]]*\]")]
    private static partial Regex ModulePlans();
}

/// <summary>The options of <c>scripts/generate</c> on the template.</summary>
internal sealed record InitOptions(IReadOnlyDictionary<string, string> Values, bool Yes, bool SkipChecks, bool NoGit)
{
    private static readonly string[] Known = ["--product", "--org", "--timezone", "--currency", "--locales", "--plans", "--cloud", "--modules"];

    public string? this[string name] => Values.GetValueOrDefault(name);

    public static InitOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool yes = false, skipChecks = false, noGit = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--yes" or "-y":
                    yes = true;
                    break;
                case "--skip-checks":
                    skipChecks = true;
                    break;
                case "--no-git":
                    noGit = true;
                    break;
                case var name when Known.Contains(name) && i + 1 < args.Length:
                    values[name] = args[++i];
                    break;
                default:
                    throw new PlatformDefinitionException($"Unknown option '{args[i]}'.\n{InitCommandUsage.Text}");
            }
        }
        return new InitOptions(values, yes, skipChecks, noGit);
    }
}

internal static class InitCommandUsage
{
    public const string Text =
        "Usage: scripts/generate [--product <key>] [--org \"Org\"] [--timezone <IANA id>] [--currency <ISO 4217>] [--locales en,ms] " +
        "[--plans standard,plus] [--cloud azure|aws] [--modules key1,key2] [--yes] [--skip-checks] [--no-git]";
}

/// <summary>What the product is called and its defaults, from the options or asked for, checked before anything changes.</summary>
internal sealed record InitAnswers(
    string Product,
    string Org,
    string TimeZone,
    string Currency,
    IReadOnlyList<string> Locales,
    IReadOnlyList<string> Plans,
    string Cloud,
    IReadOnlyList<string> Modules)
{
    /// <summary>A product key: lowercase letters and digits, starting with a letter. It becomes the C# root namespace and the npm scope.</summary>
    internal static readonly Regex ProductPattern = new("^[a-z][a-z0-9]{1,29}$", RegexOptions.CultureInvariant);

    /// <summary>Names that would collide with a framework namespace or with the template's own placeholder.</summary>
    private static readonly HashSet<string> ReservedProducts = new(StringComparer.Ordinal) { "system", "microsoft", "windows", InitCommand.TemplateProduct };

    /// <summary>Asks for every value not given as an option (unless --yes or input is redirected), offering the template's value as the default.</summary>
    public static InitAnswers Ask(PlatformDefinition template, InitOptions options)
    {
        var interactive = !options.Yes && !Console.IsInputRedirected;
        string Value(string option, string question, string fallback)
        {
            if (options[option] is { } given)
                return given.Trim();
            if (!interactive)
                return fallback;
            Console.Write(fallback.Length > 0 ? $"{question} [{fallback}]: " : $"{question}: ");
            var typed = Console.ReadLine()?.Trim();
            return string.IsNullOrEmpty(typed) ? fallback : typed;
        }

        if (interactive)
            Console.WriteLine("Scaffold a product from this template. Press Enter to keep the value in brackets.");
        var product = Value("--product", "Product key (lowercase letters and digits, e.g. farm360)", "").ToLowerInvariant();
        var answers = new InitAnswers(
            product,
            Value("--org", "Organization", template.Org),
            Value("--timezone", "Default time zone (IANA)", template.Defaults.Timezone),
            Value("--currency", "Default currency (ISO 4217)", template.Defaults.Currency).ToUpperInvariant(),
            List(Value("--locales", "Locales (comma-separated, the first is the default)", string.Join(",", template.Defaults.Locales))),
            List(Value("--plans", "Plans (comma-separated, lowest first)", string.Join(",", template.Plans))),
            Value("--cloud", "Cloud (azure or aws)", template.Cloud),
            List(Value("--modules", "First business modules (comma-separated keys, e.g. crop-planning,livestock; empty for none)", "")));
        answers.Validate(template);
        return answers;
    }

    /// <summary>A resumed run: platform.yaml already has the product's values; only the modules may still be missing.</summary>
    public static InitAnswers From(PlatformDefinition platform, InitOptions options)
    {
        var answers = new InitAnswers(platform.Product, platform.Org, platform.Defaults.Timezone, platform.Defaults.Currency,
            platform.Defaults.Locales, platform.Plans, platform.Cloud, List(options["--modules"] ?? ""));
        answers.ValidateModules(platform);
        return answers;
    }

    internal void Validate(PlatformDefinition template)
    {
        if (Product.Length == 0)
            throw new PlatformDefinitionException($"A product key is required (--product).\n{InitCommandUsage.Text}");
        if (!ProductPattern.IsMatch(Product))
            throw new PlatformDefinitionException($"'{Product}' is not a valid product key: it must match {ProductPattern} (no hyphens: it becomes a namespace).");
        if (ReservedProducts.Contains(Product))
            throw new PlatformDefinitionException($"'{Product}' can't be used as a product key.");

        // The template with these answers must be a valid platform.yaml.
        var candidate = new PlatformDefinition
        {
            Product = Product,
            Org = Org,
            Defaults = new DefaultsDefinition { Timezone = TimeZone, Currency = Currency, Locales = [.. Locales] },
            Cloud = Cloud,
            Database = template.Database,
            Roles = template.Roles,
            Plans = [.. Plans],
            Services = template.Services,
            Modules = [.. template.Modules.Select(m => new ModuleDefinition { Key = m.Key, Name = m.Name, Plans = [.. Plans] })],
        };
        candidate.Validate();
        ValidateModules(candidate);
    }

    private void ValidateModules(PlatformDefinition platform)
    {
        foreach (var module in Modules)
        {
            if (!Farm360.Core.Platform.KeyRules.Pattern.IsMatch(module) || Farm360.Core.Platform.KeyRules.Reserved.Contains(module))
                throw new PlatformDefinitionException($"'{module}' is not a valid module key: it must match {Farm360.Core.Platform.KeyRules.Pattern} and not be reserved.");
            // A business module an earlier run added (a service of the same key that owns it) is resumed; any other existing key is
            // one of the platform's own services or modules.
            var service = platform.Services.FirstOrDefault(s => s.Key == module);
            var isModule = platform.Modules.Any(m => m.Key == module);
            var addedEarlier = service is not null && isModule && service.Owns.Contains(module);
            if ((service is not null || isModule) && !addedEarlier)
                throw new PlatformDefinitionException($"'{module}' is part of the platform already; choose another key.");
        }
        if (Modules.Distinct(StringComparer.Ordinal).Count() != Modules.Count)
            throw new PlatformDefinitionException("A module is listed twice.");
    }

    private static List<string> List(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
