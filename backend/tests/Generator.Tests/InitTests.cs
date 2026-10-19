using System.Text;

namespace Saaskit.Generator.Tests;

/// <summary>
/// Scaffolding a product from the template: the codemod and the answers. Inputs are built from <see cref="InitCommand.TemplateProduct"/>,
/// never typed, because the codemod renames this file too.
/// </summary>
public sealed class InitTests : IDisposable
{
    private const string Target = "orchard";
    private static readonly string Lower = InitCommand.TemplateProduct;
    private static readonly string Pascal = Naming.Pascal(InitCommand.TemplateProduct);
    private static readonly string Upper = InitCommand.TemplateProduct.ToUpperInvariant();

    private readonly string _root = Directory.CreateTempSubdirectory("init-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string content, bool bom = false)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(bom));
        return path;
    }

    [Fact]
    public void Every_casing_is_renamed_in_file_contents()
    {
        var file = Write("src/Program.cs", $"namespace {Pascal}.Core; // @{Lower}/ui-kit {Upper}_HOME");

        var result = ProductRenamer.Run(_root, Lower, Target);

        File.ReadAllText(file).ShouldBe("namespace Orchard.Core; // @orchard/ui-kit ORCHARD_HOME");
        result.ChangedFiles.ShouldBe([Path.Combine("src", "Program.cs")]);
    }

    [Fact]
    public void Paths_are_renamed_deepest_first()
    {
        Write($"backend/{Lower}.slnx", "<Solution />");
        Write($"{Pascal}.Dir/{Pascal}.Inner/file.txt", "x");

        ProductRenamer.Run(_root, Lower, Target);

        File.Exists(Path.Combine(_root, "backend", "orchard.slnx")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "Orchard.Dir", "Orchard.Inner", "file.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_root, $"{Pascal}.Dir")).ShouldBeFalse();
    }

    [Fact]
    public void Build_output_dependencies_and_binary_files_are_left_alone()
    {
        var dependency = Write($"frontend/node_modules/@{Lower}/x/index.js", Lower);
        var output = Write($"backend/bin/{Pascal}.dll.txt", Lower);
        var binary = Path.Combine(_root, "logo.png");
        File.WriteAllBytes(binary, [.. Encoding.UTF8.GetBytes(Lower), 0, 1, 2]);

        ProductRenamer.Run(_root, Lower, Target);

        File.ReadAllText(dependency).ShouldBe(Lower);
        File.ReadAllText(output).ShouldBe(Lower);
        File.ReadAllBytes(binary)[..Lower.Length].ShouldBe(Encoding.UTF8.GetBytes(Lower));
    }

    [Fact]
    public void Renaming_again_finishes_a_half_renamed_tree_and_then_changes_nothing()
    {
        var done = Write("src/Done.cs", "namespace Orchard.Core;");
        var missed = Write("src/Missed.cs", $"namespace {Pascal}.Contracts;");

        ProductRenamer.Run(_root, Lower, Target).ChangedFiles.ShouldBe([Path.Combine("src", "Missed.cs")]);
        File.ReadAllText(done).ShouldBe("namespace Orchard.Core;");
        File.ReadAllText(missed).ShouldBe("namespace Orchard.Contracts;");

        var again = ProductRenamer.Run(_root, Lower, Target);
        again.ChangedFiles.ShouldBeEmpty();
        again.RenamedPaths.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_another_program_holds_stops_the_rename_before_anything_changes()
    {
        var first = Write("a/First.cs", Pascal);
        var held = Write($"b/{Lower}.slnx", Pascal);

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Should.Throw<PlatformDefinitionException>(() => ProductRenamer.Run(_root, Lower, Target));
            error.Message.ShouldContain($"{Lower}.slnx");
            error.Message.ShouldContain("scripts/generate again");
        }
        File.ReadAllText(first).ShouldBe(Pascal);   // untouched: all or nothing

        ProductRenamer.Run(_root, Lower, Target);   // released: the run again succeeds
        File.ReadAllText(first).ShouldBe("Orchard");
        File.Exists(Path.Combine(_root, "b", "orchard.slnx")).ShouldBeTrue();
    }

    [Fact]
    public void Module_folders_left_by_an_earlier_run_are_found()
    {
        foreach (var dir in new[] { "backend/src/Modules/Identity", "backend/src/Modules/Master.Migrations.SqlServer", "backend/src/Hosts/AllInOne.Host",
            "backend/src/Hosts/CropPlanning.Host", "backend/tests/TestSupport", "backend/tests/Master.Tests", "frontend/modules/master",
            "frontend/modules/crop-planning", "frontend/modules/lint-fixture-a" })
            Directory.CreateDirectory(Path.Combine(_root, dir));

        ModuleFolders.Strays(_root, Template(), ["crop-planning"]).ShouldBe(
            ["backend/src/Modules/Master.Migrations.SqlServer", "backend/tests/Master.Tests", "frontend/modules/master"], ignoreOrder: true);
    }

    [Fact]
    public void A_stray_module_folder_is_left_out_of_the_solution()
    {
        Write("backend/src/Hosts/Identity.Host/Identity.Host.csproj", "<Project />");
        Write("backend/src/Hosts/Master.Host/Master.Host.csproj", "<Project />");   // written back by an editor, say
        var platform = Template();
        var files = new GeneratedFiles(_root);

        Emitters.SolutionEmitter.Emit(platform, files);

        var solution = files.Files[$"backend/{platform.Product}.slnx"];
        solution.ShouldContain("src/Hosts/Identity.Host/Identity.Host.csproj");
        solution.ShouldNotContain("Master.Host");
    }

    [Fact]
    public void The_modules_asked_for_are_remembered_for_a_repeated_run()
    {
        Write(InitCommand.Marker, "This repository is the solution template.\n");

        InitCommand.RememberModules(_root, ["crop-planning", "livestock"]);
        InitCommand.RememberedModules(_root).ShouldBe(["crop-planning", "livestock"]);
        File.ReadAllText(Path.Combine(_root, InitCommand.Marker)).ShouldStartWith("This repository is the solution template.");

        // A repeated run with no options adds them; --modules on it adds more.
        var resumed = InitAnswers.From(Template(), InitOptions.Parse(["--modules", "orchards"]), InitCommand.RememberedModules(_root));
        resumed.Modules.ShouldBe(["crop-planning", "livestock", "orchards"]);
    }

    [Fact]
    public void A_byte_order_mark_is_kept()
    {
        var file = Write("a.cs", Pascal, bom: true);

        ProductRenamer.Run(_root, Lower, Target);

        File.ReadAllBytes(file).ShouldBe([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("Orchard")]);
    }

    private static InitAnswers Answers(string product = Target, string[]? modules = null, string[]? plans = null) =>
        new(product, "Orchard Co", "Asia/Kuala_Lumpur", "MYR", ["en", "ms"], plans ?? ["basic", "pro"], "azure", modules ?? ["crop-planning"]);

    private static PlatformDefinition Template() => PlatformDefinition.Load(Path.Combine(RepoRoot.Find(), "platform.yaml"));

    [Fact]
    public void Valid_answers_pass() => Should.NotThrow(() => Answers().Validate(Template()));

    [Theory]
    [InlineData("")]
    [InlineData("Orchard")]
    [InlineData("farm-360")]
    [InlineData("360farm")]
    [InlineData("system")]
    public void Product_keys_are_lowercase_letters_and_digits_and_not_reserved(string product) =>
        Should.Throw<PlatformDefinitionException>(() => Answers(product).Validate(Template()));

    [Fact]
    public void The_template_placeholder_is_not_a_product() =>
        Should.Throw<PlatformDefinitionException>(() => Answers(InitCommand.TemplateProduct).Validate(Template()));

    [Theory]
    [InlineData("Crops")]
    [InlineData("api")]
    [InlineData("identity")]
    [InlineData("tenant-settings")]
    public void Module_keys_follow_the_key_rules_and_are_new(string module) =>
        Should.Throw<PlatformDefinitionException>(() => Answers(modules: [module]).Validate(Template()));

    [Fact]
    public void Invalid_defaults_are_refused_before_anything_changes() =>
        Should.Throw<PlatformDefinitionException>(() => (Answers() with { Currency = "ringgit" }).Validate(Template()));

    [Fact]
    public void A_key_the_platform_already_uses_as_a_name_is_refused()
    {
        // SQL Server's system database: a module named "master" would fail the hard-coded-key architecture test.
        KeyCollisions.Find(RepoRoot.Find(), "master").ShouldNotBeEmpty();
        Should.Throw<PlatformDefinitionException>(() => KeyCollisions.Check(RepoRoot.Find(), "master"));
    }

    [Fact]
    public void An_ordinary_key_does_not_collide() => KeyCollisions.Find(RepoRoot.Find(), "crop-planning").ShouldBeEmpty();

    [Fact]
    public void Template_only_sections_are_removed_from_the_readme()
    {
        var readme = $"# Title\n{InitCommand.TemplateSectionStart}\nStart a product.\n{InitCommand.TemplateSectionEnd}\n## Kept\n";

        InitCommand.WithoutTemplateSections(readme).ShouldBe("# Title\n## Kept\n");
    }

    [Fact]
    public void The_answers_replace_the_values_in_platform_yaml_and_keep_its_comments()
    {
        var path = Path.Combine(_root, "platform.yaml");
        File.Copy(Path.Combine(RepoRoot.Find(), "platform.yaml"), path);

        InitCommand.WritePlatformYaml(path, Answers());

        var text = File.ReadAllText(path);
        text.ShouldContain("product: orchard\n");
        text.ShouldContain("org: Orchard Co\n");
        text.ShouldContain("  locales: [en, ms]\n");
        text.ShouldContain("# services and modules answer two different questions:");
        var written = PlatformDefinition.Load(path);
        written.Plans.ShouldBe(["basic", "pro"]);
        written.Modules.ShouldAllBe(m => m.Plans.Count == 2 && m.Plans[0] == "basic" && m.Plans[1] == "pro");
        written.Services.Select(s => s.Key).ShouldBe(Template().Services.Select(s => s.Key));
    }
}
