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

    public void Dispose()
    {
        // git keeps its objects read-only, and Directory.Delete won't remove read-only files.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

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
    public void A_contracts_folder_left_by_a_removed_module_is_found()
    {
        foreach (var dir in new[] { "backend/src/Contracts/Modules/Master", "backend/src/Contracts/Modules/CropPlanning", "backend/src/Contracts/Customization" })
            Directory.CreateDirectory(Path.Combine(_root, dir));

        ModuleFolders.Strays(_root, Template(), ["crop-planning"]).ShouldBe(["backend/src/Contracts/Modules/Master"]);
    }

    [Fact]
    public void A_key_whose_contracts_folder_exists_is_refused()
    {
        Directory.CreateDirectory(Path.Combine(_root, "backend", "src", "Contracts", "Modules", "CropPlanning"));

        Should.Throw<PlatformDefinitionException>(() => KeyCollisions.Check(_root, "crop-planning")).Message.ShouldContain("Contracts/Modules/CropPlanning");
    }

    [Fact]
    public void A_modules_contracts_are_written_from_the_template_with_its_key_and_name()
    {
        var template = Path.Combine("templates", "backend-module", "src", "Contracts", "Modules", "Sample");
        Directory.CreateDirectory(Path.Combine(_root, template));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), template), "*.cs"))
            File.Copy(file, Path.Combine(_root, template, Path.GetFileName(file)));

        NewModuleCommand.CopyContracts(_root, "crop-planning", "CropPlanning");

        var folder = NewModuleCommand.ContractsFolder(_root, "CropPlanning");
        Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order().ShouldBe(["CropPlanningClient.cs", "CropPlanningEvents.cs", "CropPlanningViews.cs"]);
        var events = File.ReadAllText(Path.Combine(folder, "CropPlanningEvents.cs"));
        events.ShouldContain("crop-planning.item-created");
        events.ShouldContain(".Contracts.CropPlanning;");
        File.ReadAllText(Path.Combine(folder, "CropPlanningClient.cs")).ShouldContain("ModuleKeys.CropPlanning");
        Directory.EnumerateFiles(folder).Select(File.ReadAllText).ShouldAllBe(text => !text.Contains("Sample") && !text.Contains("sample-key"));
    }

    [Fact]
    public void A_platform_module_cant_be_removed()
    {
        var platform = Template();
        platform.Modules.RemoveAll(m => m.Key == "billing");
        platform.Services.Single(s => s.Key == "tenancy").Owns.Remove("billing");

        Should.Throw<PlatformDefinitionException>(platform.RequirePlatformParts).Message.ShouldContain("'billing' is part of the platform");
    }

    [Fact]
    public void A_module_declared_in_platform_yaml_by_hand_is_found_until_it_is_scaffolded()
    {
        var platform = Template();
        // The business modules platform.yaml already lists (Base, and a product's own) are on disk, so only the one added by hand is unscaffolded.
        foreach (var service in platform.Services.Where(s => s.Owns.Contains(s.Key)))
            Directory.CreateDirectory(Path.Combine(_root, "backend", "src", "Modules", Naming.Pascal(service.Key)));
        platform.Services.Add(new ServiceDefinition { Key = "purchase", Port = 5004, Owns = ["purchase"] });
        platform.Modules.Add(new ModuleDefinition { Key = "purchase", Name = "Purchase", Plans = [.. platform.Plans] });

        NewModuleCommand.Unscaffolded(_root, platform).ShouldBe(["purchase"]);

        Directory.CreateDirectory(Path.Combine(_root, "backend", "src", "Modules", "Purchase"));
        NewModuleCommand.Unscaffolded(_root, platform).ShouldBeEmpty();
    }

    [Fact]
    public void Stray_module_folders_are_deleted()
    {
        Write("backend/src/Hosts/Master.Host/Master.Host.csproj", "<Project />");
        Write("backend/src/Hosts/Master.Host/obj/x.json", "{}");

        ModuleFolders.Delete(_root, ModuleFolders.Strays(_root, Template(), []));

        Directory.Exists(Path.Combine(_root, "backend", "src", "Hosts", "Master.Host")).ShouldBeFalse();
    }

    [Fact]
    public void A_stray_folder_with_committed_files_is_kept_and_stops_the_run()
    {
        Write("backend/tests/Master.Tests/Master.Tests.csproj", "<Project />");
        foreach (var args in new[] { new[] { "init", "--quiet" }, ["add", "--all"], ["-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "x"] })
            Processes.Capture("git", _root, "", args);

        var error = Should.Throw<PlatformDefinitionException>(() => ModuleFolders.Delete(_root, ModuleFolders.Strays(_root, Template(), [])));

        error.Message.ShouldContain("backend/tests/Master.Tests");
        File.Exists(Path.Combine(_root, "backend", "tests", "Master.Tests", "Master.Tests.csproj")).ShouldBeTrue();
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

        InitCommand.RememberModules(_root, [new("crop-planning"), new("livestock", "advanced")]);
        InitCommand.RememberedModules(_root).ShouldBe([new("crop-planning"), new("livestock", "advanced")]);
        File.ReadAllText(Path.Combine(_root, InitCommand.Marker)).ShouldStartWith("This repository is the solution template.");

        // A repeated run with no options adds them; --modules on it adds more.
        var resumed = InitAnswers.From(Template(), InitOptions.Parse(["--modules", "orchards"]), InitCommand.RememberedModules(_root));
        resumed.Modules.ShouldBe([new("crop-planning"), new("livestock", "advanced"), new("orchards")]);
    }

    [Fact]
    public void A_byte_order_mark_is_kept()
    {
        var file = Write("a.cs", Pascal, bom: true);

        ProductRenamer.Run(_root, Lower, Target);

        File.ReadAllBytes(file).ShouldBe([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("Orchard")]);
    }

    private static InitAnswers Answers(string product = Target, string[]? modules = null, string[]? plans = null) =>
        new(product, "Orchard Co", "Asia/Kuala_Lumpur", "MYR", ["en", "ms"], plans ?? ["basic", "pro"], "azure", ModuleChoice.ParseList(string.Join(',', modules ?? ["crop-planning"])));

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
    public void A_module_can_name_its_lowest_plan_and_the_plan_must_exist()
    {
        Should.NotThrow(() => Answers(modules: ["crop-planning:pro", "livestock"]).Validate(Template()));
        Should.Throw<PlatformDefinitionException>(() => Answers(modules: ["crop-planning:gold"]).Validate(Template())).Message.ShouldContain("'gold' is not one of the plans (basic, pro)");
    }

    [Fact]
    public void The_modules_option_reads_key_and_plan_pairs()
    {
        var parsed = ModuleChoice.ParseList("patients, appointments:advanced ,pharmacy:complete");

        parsed.ShouldBe([new("patients"), new("appointments", "advanced"), new("pharmacy", "complete")]);
        parsed.Select(m => m.ToString()).ShouldBe(["patients", "appointments:advanced", "pharmacy:complete"]);
    }

    [Theory]
    [InlineData(null, new[] { "lite", "advanced", "complete" })]
    [InlineData("lite", new[] { "lite", "advanced", "complete" })]
    [InlineData("advanced", new[] { "advanced", "complete" })]
    [InlineData("complete", new[] { "complete" })]
    public void A_module_is_in_its_lowest_plan_and_every_plan_above_it(string? fromPlan, string[] expected) =>
        NewModuleCommand.PlansFrom(["lite", "advanced", "complete"], fromPlan).ShouldBe(expected);

    [Fact]
    public void An_unknown_lowest_plan_is_refused() =>
        Should.Throw<PlatformDefinitionException>(() => NewModuleCommand.PlansFrom(["lite", "advanced"], "gold")).Message.ShouldContain("not one of the plans");

    [Fact]
    public void A_new_module_is_written_into_platform_yaml_with_its_plans()
    {
        var path = Path.Combine(_root, "platform.yaml");
        File.Copy(Path.Combine(RepoRoot.Find(), "platform.yaml"), path);

        // A key and port no product uses, so the test also holds after a product has added its own modules.
        var port = PlatformDefinition.Load(path).Services.Max(s => s.Port) + 1;
        NewModuleCommand.AddToPlatformYaml(path, "plan-check", "Plan Check", port, NewModuleCommand.PlansFrom(["lite", "advanced", "complete"], "complete"));

        var written = PlatformDefinition.Load(path);
        written.Modules.Single(m => m.Key == "plan-check").Plans.ShouldBe(["complete"]);
        written.Modules.Single(m => m.Key == "base").Plans.ShouldBe(["lite", "advanced", "complete"]);
    }

    [Fact]
    public void A_platform_module_must_be_in_every_plan()
    {
        var platform = Template();
        platform.Modules.Single(m => m.Key == "billing").Plans = ["advanced", "complete"];

        Should.Throw<PlatformDefinitionException>(platform.RequirePlatformParts).Message.ShouldContain("'billing' is part of the platform and must be in every plan (missing: lite)");
    }

    [Fact]
    public void The_base_module_is_a_platform_module()
    {
        PlatformDefinition.IsPlatformModule("base").ShouldBeTrue();
        PlatformDefinition.IsPlatformModule("patients").ShouldBeFalse();
        var platform = Template();
        platform.Modules.Single(m => m.Key == "base").Plans = ["complete"];

        Should.Throw<PlatformDefinitionException>(platform.RequirePlatformParts).Message.ShouldContain("'base'");
    }

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
