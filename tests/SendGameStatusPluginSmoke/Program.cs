using System.Xml.Linq;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using SendGameStatusPlugin;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;
using TargetPlugin = SendGameStatusPlugin.SendGameStatusPlugin;

using var ui = new WorkspaceSmokeSession();

AssertWorkspaceLifecycle(ui);
AssertAnalyzersAreSplitIntoFolder();
AssertAnalyzerPathDoesNotWriteRawAnsiConsole();
AssertAnalyzerPathDoesNotUseConsoleInteractionOutput();
AssertGameStatusOutputTargetsPluginScenarioDirectories();
AssertProjectFileUsesRootBuildConvention();

Console.WriteLine("PASS SendGameStatusPlugin smoke");

static void AssertWorkspaceLifecycle(WorkspaceSmokeSession ui)
{
    var originalDirectory = Directory.GetCurrentDirectory();
    var tempDirectory = Path.Combine(Path.GetTempPath(), "SendGameStatusPluginSmoke", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    ui.Bootstrap.SwitchTo();
    var baseline = ui.CaptureScreen();
    try
    {
        Directory.SetCurrentDirectory(tempDirectory);
        IPlugin plugin = new TargetPlugin();
        var context = new SmokePluginContext(ui.Application);
        plugin.Initialize(context);
        try
        {
            AssertProgrammaticLegendRegistrations(context.Analyzers);
            AssertExactAnalyzerDispatch(plugin);
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap)
                || !string.Equals(baseline, ui.CaptureScreen(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Initialize must not create a workspace or change the visible framebuffer.");
            }

            var outputDirectory = Path.Combine(
                tempDirectory,
                "PluginData",
                "SendGameStatusPlugin",
                nameof(GameStatusSend_Onsen));
            if (!File.Exists(Path.Combine(outputDirectory, "thisTurn.json"))
                || !File.Exists(Path.Combine(outputDirectory, "turn7.json")))
            {
                throw new InvalidOperationException("A real scenario write did not produce both status files.");
            }
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap)
                || !string.Equals(baseline, ui.CaptureScreen(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A scenario write changed the current workspace or visible framebuffer.");
            }
        }
        finally
        {
            plugin.Dispose();
        }

        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap)
            || !string.Equals(baseline, ui.CaptureScreen(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dispose changed workspace state or the visible framebuffer.");
        }

    }
    finally
    {
        Directory.SetCurrentDirectory(originalDirectory);
        Directory.Delete(tempDirectory, recursive: true);
    }
}

static void AssertExactAnalyzerDispatch(IPlugin plugin)
{
    var onsenCheckEvent = CreateWritableOnsenResponse();
    var cases = new (Type EndpointType, int Priority, object Response)[]
    {
        (typeof(GameApi.SingleModeArc.CheckEvent), 2, new SingleModeArcCheckEventResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeArc.ExecCommand), 2, new SingleModeArcExecCommandResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeSport.CheckEvent), 2, new SingleModeSportCheckEventResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeSport.ExecCommand), 2, new SingleModeSportExecCommandResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeCook.CheckEvent), 2, new SingleModeCookCheckEventResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeCook.ExecCommand), 2, new SingleModeCookExecCommandResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeOnsen.CheckEvent), 0, onsenCheckEvent),
        (typeof(GameApi.SingleModeOnsen.CheckEvent), 2, onsenCheckEvent),
        (typeof(GameApi.SingleModeOnsen.ExecCommand), 2, new SingleModeOnsenExecCommandResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeMecha.CheckEvent), 2, new SingleModeMechaCheckEventResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
        (typeof(GameApi.SingleModeMecha.ExecCommand), 2, new SingleModeMechaExecCommandResponse
        {
            data = new() { chara_info = new() { state = 2 }, unchecked_event_array = [] }
        }),
    };
    var registrations = PluginManager.CreateRegistrationPlan(plugin).Analyzers
        .Where(registration => registration.Kind == AnalyzerKind.Response)
        .ToArray();
    if (registrations.Length != cases.Length)
        throw new InvalidOperationException(
            $"SendGameStatusPlugin must expose exactly 11 exact response analyzers, got {registrations.Length}.");

    foreach (var item in cases)
    {
        var registration = registrations.Single(candidate =>
            candidate.EndpointType == item.EndpointType && candidate.Priority == item.Priority);
        var endpoint = GameEndpointCatalog.ByEndpointType[item.EndpointType];
        if (endpoint.ResponseType != item.Response.GetType())
            throw new InvalidOperationException(
                $"{item.EndpointType.FullName} expects {endpoint.ResponseType.Name}, got {item.Response.GetType().Name}.");

        registration.Handler(new(
                endpoint,
                MessagePackSerializer.Serialize(item.Response.GetType(), item.Response),
                new(null, null, null, null, null, null)))
            .GetAwaiter()
            .GetResult();
    }
}

static SingleModeOnsenCheckEventResponse CreateWritableOnsenResponse()
{
    int[] trainingIds = [101, 105, 102, 103, 106];
    return new()
    {
        data = new()
        {
            chara_info = new()
            {
                state = 1,
                playing_state = 1,
                scenario_id = 12,
                turn = 8,
                card_id = 100100,
                rarity = 5,
                vital = 80,
                max_vital = 100,
                motivation = 5,
                speed = 500,
                stamina = 500,
                power = 500,
                guts = 500,
                wiz = 500,
                max_speed = 1200,
                max_stamina = 1200,
                max_power = 1200,
                max_guts = 1200,
                max_wiz = 1200,
                skill_point = 100,
                skill_array = [],
                skill_tips_array = [],
                support_card_array = [],
                evaluation_info_array =
                [
                    .. Enumerable.Range(1, 6).Select(targetId => new EvaluationInfo
                    {
                        target_id = targetId,
                        evaluation = 0
                    })
                ],
                training_level_info_array =
                [
                    .. trainingIds.Select(commandId => new TrainingLevelInfo
                    {
                        command_id = commandId,
                        level = 1
                    })
                ],
                chara_effect_id_array = []
            },
            home_info = new()
            {
                command_info_array =
                [
                    .. trainingIds.Select(commandId => new SingleModeCommandInfo
                    {
                        command_type = 1,
                        command_id = commandId,
                        is_enable = 1,
                        training_partner_array = [],
                        tips_event_partner_array = [],
                        sub_command_partner_array = []
                    })
                ]
            },
            unchecked_event_array = [],
            race_start_info = null!
        }
    };
}

static void AssertProgrammaticLegendRegistrations(SmokeAnalyzerRegistry registry)
{
    var expected = new[]
    {
        new AnalyzerRegistrationExpectation(
            typeof(SingleModeLegendCheckEventResponse),
            EndpointPatternKind.Regex,
            "^/umamusume/single_mode_legend/(?:change_short_cut|check_event|cm_end|continue|exec_command|finish_claw_crane|gain_skills|legend_race_continue|legend_race_end|legend_race_entry|legend_race_out|legend_race_start|popularity_end|race_end|race_entry|race_out)$"),
        new AnalyzerRegistrationExpectation(
            typeof(SingleModeLegendLoadResponse),
            EndpointPatternKind.Exact,
            "/umamusume/single_mode_legend/load"),
    };

    if (registry.Registrations.Count != expected.Length)
        throw new InvalidOperationException(
            $"SendGameStatusPlugin must register exactly two Legend analyzers, got {registry.Registrations.Count}.");

    foreach (var (registration, expectation) in registry.Registrations.Zip(expected))
    {
        if (registration.Kind != AnalyzerKind.Response
            || registration.PayloadType != expectation.PayloadType
            || registration.Priority != 2
            || registration.Patterns is not [{ Kind: var kind, Pattern: var pattern }]
            || kind != expectation.PatternKind
            || !string.Equals(pattern, expectation.Pattern, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected Legend registration for {registration.PayloadType.Name}: " +
                $"{registration.Kind}, priority {registration.Priority}, " +
                $"patterns [{string.Join(", ", registration.Patterns)}].");
        }
    }
}

static void AssertAnalyzersAreSplitIntoFolder()
{
    var repoRoot = FindRepositoryRoot();
    var pluginRoot = Path.Combine(repoRoot, "SendGameStatusPlugin");
    var classSource = File.ReadAllText(Path.Combine(pluginRoot, "Class1.cs"));
    if (classSource.Contains("[ResponseAnalyzer", StringComparison.Ordinal))
        throw new InvalidOperationException("SendGameStatusPlugin Class1.cs must not contain analyzer registrations.");

    var analyzerDirectory = Path.Combine(pluginRoot, "Analyzers");
    var expectedFiles = new[]
    {
        "AnalyzerHelpers.cs",
        "ArcAnalyzers.cs",
        "CookAnalyzers.cs",
        "LegendAnalyzers.cs",
        "MechaAnalyzers.cs",
        "OnsenAnalyzers.cs",
        "UafAnalyzers.cs",
    };
    foreach (var file in expectedFiles)
    {
        if (!File.Exists(Path.Combine(analyzerDirectory, file)))
            throw new InvalidOperationException($"SendGameStatusPlugin analyzer file missing: Analyzers\\{file}");
    }
}

static void AssertAnalyzerPathDoesNotWriteRawAnsiConsole()
{
    var repoRoot = FindRepositoryRoot();
    var pluginRoot = Path.Combine(repoRoot, "SendGameStatusPlugin");
    var sourceFiles = Directory
        .EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !IsBuildArtifactPath(pluginRoot, path))
        .Where(path => !string.Equals(Path.GetFileName(path), "Class1.cs", StringComparison.Ordinal))
        .ToArray();

    foreach (var sourceFile in sourceFiles)
    {
        var source = File.ReadAllText(sourceFile);
        if (source.Contains("AnsiConsole.MarkupLine", StringComparison.Ordinal) ||
            source.Contains("AnsiConsole.WriteLine", StringComparison.Ordinal) ||
            source.Contains("AnsiConsole.Write(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SendGameStatusPlugin analyzer path must not write raw AnsiConsole output: {Path.GetRelativePath(pluginRoot, sourceFile)}");
        }
    }
}

static void AssertAnalyzerPathDoesNotUseConsoleInteractionOutput()
{
    var repoRoot = FindRepositoryRoot();
    var pluginRoot = Path.Combine(repoRoot, "SendGameStatusPlugin");
    var sourceFiles = Directory
        .EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !IsBuildArtifactPath(pluginRoot, path))
        .Where(path => !string.Equals(Path.GetFileName(path), "Class1.cs", StringComparison.Ordinal))
        .ToArray();

    var forbiddenCalls = new[]
    {
        "TerminalUi.MarkupLine",
        "TerminalUi.WriteLine",
        "TerminalUi.Write(",
        "TerminalUi.Clear(",
        "TerminalUi.Prompt",
        "TerminalUi.ReadKey",
        "TerminalUi.ReadLine",
    };

    foreach (var sourceFile in sourceFiles)
    {
        var source = File.ReadAllText(sourceFile);
        foreach (var forbiddenCall in forbiddenCalls)
        {
            if (!source.Contains(forbiddenCall, StringComparison.Ordinal))
                continue;

            throw new InvalidOperationException(
                $"SendGameStatusPlugin analyzer path must not use console interaction output: {Path.GetRelativePath(pluginRoot, sourceFile)} contains {forbiddenCall}");
        }
    }
}

static void AssertGameStatusOutputTargetsPluginScenarioDirectories()
{
    var repoRoot = FindRepositoryRoot();
    var pluginRoot = Path.Combine(repoRoot, "SendGameStatusPlugin");
    var sourceFiles = Directory
        .EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !IsBuildArtifactPath(pluginRoot, path))
        .ToArray();

    foreach (var sourceFile in sourceFiles)
    {
        var source = File.ReadAllText(sourceFile);
        if (source.Contains("WriteLocalGameData", StringComparison.Ordinal) ||
            source.Contains("\"GameData\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SendGameStatusPlugin must write scenario data under PluginData\\SendGameStatusPlugin, not GameData: {Path.GetRelativePath(pluginRoot, sourceFile)}");
        }
    }

}

static bool IsBuildArtifactPath(string root, string path)
{
    var relative = Path.GetRelativePath(root, path);
    return relative
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => segment is "bin" or "obj" or "bin-test" or "obj-test");
}

static void AssertProjectFileUsesRootBuildConvention()
{
    var repoRoot = FindRepositoryRoot();
    var projectPath = Path.Combine(repoRoot, "SendGameStatusPlugin", "SendGameStatusPlugin.csproj");
    var document = XDocument.Load(projectPath);

    var isUraPlugin = document.Descendants("IsUraPlugin").SingleOrDefault()?.Value;
    if (!string.Equals(isUraPlugin, "true", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("SendGameStatusPlugin.csproj must set IsUraPlugin=true.");
    if (document.Descendants("PluginDependencies").SingleOrDefault()?.Value != "EventLoggerPlugin")
        throw new InvalidOperationException("SendGameStatusPlugin.csproj must declare EventLoggerPlugin as a plugin dependency.");

    var forbiddenPackages = document
        .Descendants("PackageReference")
        .Select(x => x.Attribute("Include")?.Value)
        .Where(x => x is "Gallop" or "UmamusumeResponseAnalyzer.Plugin.Abstractions")
        .ToArray();
    if (forbiddenPackages.Length != 0)
        throw new InvalidOperationException($"SendGameStatusPlugin.csproj has forbidden package references: {string.Join(", ", forbiddenPackages)}.");

    var forbiddenProjects = document
        .Descendants("ProjectReference")
        .Select(x => x.Attribute("Include")?.Value ?? "")
        .Where(x =>
            x.Contains(@"UmamusumeResponseAnalyzer\UmamusumeResponseAnalyzer", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("SkillTipsResponseAnalyzer", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (forbiddenProjects.Length != 0)
        throw new InvalidOperationException($"SendGameStatusPlugin.csproj has forbidden project references: {string.Join(", ", forbiddenProjects)}.");

    var legacyManifestPath = Path.Combine(repoRoot, "SendGameStatusPlugin", "manifest.json");
    if (File.Exists(legacyManifestPath))
        throw new InvalidOperationException("SendGameStatusPlugin must not rely on a source manifest.json.");
}

static string FindRepositoryRoot()
{
    foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(startPath);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "SendGameStatusPlugin")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory.FullName;
            }
        }
    }

    throw new InvalidOperationException("Cannot locate URA-Plugins repository root.");
}

sealed class SmokePluginContext(IApplication application) : IPluginContext
{
    public IApplication Application { get; } = application;
    public IPluginHostEvents Events { get; } = new SmokeHostEvents();
    public SmokeAnalyzerRegistry Analyzers { get; } = new();
    IPluginAnalyzerRegistry IPluginContext.Analyzers => Analyzers;
    public bool IsPluginAvailable(string internalName) => false;

    public void RunBackground(Func<CancellationToken, ValueTask> operation)
        => throw new InvalidOperationException("SendGameStatusPlugin must not start background work.");
}

sealed class SmokeHostEvents : IPluginHostEvents
{
    public void OnStarted(Func<CancellationToken, ValueTask> handler)
        => throw new InvalidOperationException("SendGameStatusPlugin must not register a host-start callback.");
}

sealed class SmokeAnalyzerRegistry : IPluginAnalyzerRegistry
{
    public List<AnalyzerRegistration> Registrations { get; } = [];

    public void Register<TPayload>(
        AnalyzerKind kind,
        IReadOnlyList<EndpointPattern> patterns,
        Func<AnalyzerInvocation<TPayload>, ValueTask> handler,
        int priority = 0)
        => Registrations.Add(new(typeof(TPayload), kind, [.. patterns], handler, priority));
}

sealed record AnalyzerRegistration(
    Type PayloadType,
    AnalyzerKind Kind,
    EndpointPattern[] Patterns,
    Delegate Handler,
    int Priority);

sealed record AnalyzerRegistrationExpectation(
    Type PayloadType,
    EndpointPatternKind PatternKind,
    string Pattern);
