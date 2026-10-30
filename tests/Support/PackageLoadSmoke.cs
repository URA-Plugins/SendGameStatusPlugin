using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json;
using Terminal.Gui.App;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using GameText = UmamusumeResponseAnalyzer.Localization.Game;

static class PackageLoadSmoke
{
    static readonly GameHttpHeaders Headers = new("zip-smoke-sid", "zip-smoke-app", "zip-smoke-res", "zip-smoke-viewer", null, null);
    public static async Task RunAsync(WorkspaceSmokeSession ui)
    {
        var names = new[] { "SendGameStatusPlugin", "EventLoggerPlugin", "RamenScenarioAnalyzer" };
        var packages = FindPackages(names);
        using var directory = new TempCurrentDirectory("SendGameStatusPlugin-fresh-zip");
        await InitializeSmokeDatabase();
        Directory.CreateDirectory("Plugins");
        foreach (var (id, path) in packages)
            File.Copy(path, Path.Combine("Plugins", id + ".zip"));
        try
        {
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            await PluginManager.TriggerStartedAsync();
            Require(PluginManager.FailedPlugins.Count == 0 && PluginManager.LoadedPlugins.Count == names.Length,
                $"ZIP load failed: loaded={PluginManager.LoadedPlugins.Count}, failed={string.Join(",", PluginManager.FailedPlugins)}");
            foreach (var id in names)
            {
                var plugin = PluginManager.LoadedPlugins.Single(plugin => PluginManager.InternalName(plugin) == id);
                var assembly = plugin.GetType().Assembly;
                var context = AssemblyLoadContext.GetLoadContext(assembly)!;
                var metadata = PluginManager.Metadatas[id];
                Require(metadata.PackagePath == Path.GetFullPath(Path.Combine("Plugins", id + ".zip")),
                    $"{id} did not identify the isolated fresh ZIP.");
                using var archive = ZipFile.OpenRead(packages[id]);
                using var stream = archive.GetEntry(metadata.AssemblyEntry)!.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                bytes.Position = 0;
                using var pe = new PEReader(bytes);
                var reader = pe.GetMetadataReader();
                var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
                Require(!context.IsCollectible && context != AssemblyLoadContext.Default && assembly.ManifestModule.ModuleVersionId == mvid,
                    $"{id} did not execute the fresh ZIP main DLL in a fixed context.");
                foreach (var shared in new[] { typeof(IPlugin).Assembly, typeof(SingleModeChara).Assembly, typeof(IApplication).Assembly })
                    Require(ReferenceEquals(context.LoadFromAssemblyName(shared.GetName()), shared),
                        $"{id} duplicated shared Host identity {shared.GetName().Name}.");
                Console.WriteLine($"PASS ZIP origin {id}: MVID={mvid}");
            }
            Require(PluginManager.LoadedPlugins.Select(plugin => AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)).Distinct().Count() == 1,
                "Consumer and dependency ZIPs did not share one load context.");
            AssertRamenAndStatus(ui);
        }
        finally
        {
            await PluginManager.ShutdownAsync();
            ui.Flush();
        }
        Require(PluginManager.LoadedPlugins.Count == 0 && PluginManager.Contexts.Count == 0
            && PluginManager.RequestAnalyzerMethods.Count == 0 && PluginManager.ResponseAnalyzerMethods.Count == 0,
            "ZIP shutdown left plugin instances or analyzer registrations published.");
        Workspace.Create("RamenScenarioAnalyzer").SwitchTo();
        Require(!ui.CaptureScreen(200, 80).Contains("URAF", StringComparison.Ordinal), "ZIP shutdown left dependency output mounted.");
        ui.Bootstrap.SwitchTo();
    }

    static Dictionary<string, string> FindPackages(string[] names)
    {
        var root = Environment.GetEnvironmentVariable("URA_PLUGIN_SMOKE_PACKAGE_ROOT");
        if (root is null && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            root = Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")
                ?? throw new InvalidOperationException("GITHUB_WORKSPACE is required in CI."), "artifacts");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("URA_PLUGIN_SMOKE_PACKAGE_ROOT must identify the absolute fresh package output directory.");
        var packages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
        {
            using var zip = ZipFile.OpenRead(path);
            using var stream = (zip.GetEntry("manifest.json")
                ?? throw new InvalidDataException($"Package has no root manifest.json: {path}")).Open();
            using var json = JsonDocument.Parse(stream);
            var name = json.RootElement.GetProperty("InternalName").GetString()!;
            if (!names.Contains(name, StringComparer.Ordinal))
                continue;
            var manifest = PluginPackageValidator.Validate(path, true).Manifest;
            var pluginRoot = Environment.GetEnvironmentVariable("URA_TEST_PLUGIN_ROOT")
                ?? throw new InvalidOperationException("URA_TEST_PLUGIN_ROOT must identify the plugin checkout.");
            var projectPath = name switch
            {
                "SendGameStatusPlugin" => "SendGameStatusPlugin.csproj",
                "EventLoggerPlugin" => "deps/EventLoggerPlugin/EventLoggerPlugin.csproj",
                "RamenScenarioAnalyzer" => "deps/RamenScenarioAnalyzer/RamenScenarioAnalyzer.csproj",
                _ => throw new InvalidOperationException($"Unexpected package identity {name}.")
            };
            var declaration = XDocument.Load(Path.Combine(pluginRoot, projectPath))
                .Descendants("PluginDependencies").SingleOrDefault()?.Value;
            var expectedDependencies = declaration?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (!manifest.Dependencies.ToHashSet(StringComparer.Ordinal).SetEquals(expectedDependencies))
                throw new InvalidDataException($"{name} manifest does not declare its expected optional plugin dependencies.");
            if (!packages.TryAdd(name, path))
                throw new InvalidDataException($"More than one fresh ZIP identifies {name}.");
        }
        foreach (var name in names)
            if (!packages.ContainsKey(name))
                throw new FileNotFoundException($"Fresh package for {name} is missing from {root}.");
        return packages;
    }

    static void AssertRamenAndStatus(WorkspaceSmokeSession ui)
    {
        var response = PackageRamenFixture.CreateRamenLoadResponse();
        var chara = response.data.single_mode_load_common.chara_info;
        PrepareChara(chara, 14);
        chara.single_mode_chara_id = 904;
        response.data.ramen_data_set_load = new()
        {
            selected_region_id_array = [1, 2, 3],
            reduce_base_turn_info_array = [new() { feeling_id = 1, reduce_base_turn = 4 }],
            last_tasting_info = new() { region_id = 4 },
            check_point_pt = 123,
            expected_check_point_pt = 456
        };
        DispatchResponse(typeof(GameApi.SingleModeRamen.Load), response, ui);
        AssertPanel(ui, "RamenScenarioAnalyzer", "URAF", GameText.I18N_Speed, "100", "110");
        var assembly = PluginManager.LoadedPlugins.Single(plugin => PluginManager.InternalName(plugin) == "RamenScenarioAnalyzer").GetType().Assembly;
        var state = assembly.GetType("RamenScenarioAnalyzer.RamenScenarioState", true)!.GetMethod("Snapshot")!.Invoke(null, null)!;
        Require(Equals(Member(state, "loaded"), true) && Equals(Member(state, "single_mode_chara_id"), 904)
            && Equals(Member(state, "check_point_pt"), 123)
            && ((int[])Member(state, "selected_region_id_array")).SequenceEqual([1, 2, 3]),
            "Ramen ZIP did not retain the load fixture state.");
        var output = Path.Combine("PluginData", "SendGameStatusPlugin");
        var currentText = File.ReadAllText(Path.Combine(output, "thisTurn.json"));
        using var json = JsonDocument.Parse(currentText);
        var root = json.RootElement;
        var game = root.GetProperty("baseGame");
        var ramen = root.GetProperty("ramen");
        Require(game.GetProperty("single_mode_chara_id").GetInt32() == 904
            && game.GetProperty("scenarioId").GetInt32() == 14 && game.GetProperty("source").GetString() == "load"
            && ramen.GetProperty("last_ramen").GetInt32() == 3 && ramen.GetProperty("special_feeling").GetInt32() == 2
            && ramen.GetProperty("scenario_pt").GetInt32() == 123
            && ramen.GetProperty("selected_regions").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual([0, 1, 2]),
            "SendGameStatus ZIP output did not preserve character, source and Ramen state.");
        var numbered = Path.Combine(output, "game904_turn1.json");
        Require(File.ReadAllText(numbered) == currentText, "SendGameStatus numbered snapshot differs from thisTurn.json.");
        Console.WriteLine("PASS ZIP business RamenScenarioAnalyzer: state, character and URAF panel");
        Console.WriteLine("PASS ZIP business SendGameStatusPlugin: thisTurn.json + numbered snapshot, shared Ramen state");
    }

    static void PrepareChara(SingleModeChara chara, int scenario)
    {
        // The status-writer fixture requires a valid character and six evaluation slots.
        chara.state = 1;
        chara.scenario_id = scenario;
        chara.card_id = 100100;
        chara.rarity = 5;
        chara.skill_array = [];
        chara.skill_tips_array = [];
        chara.skill_upgrade_info_array = [];
        chara.chara_effect_id_array = [];
        chara.evaluation_info_array = [.. Enumerable.Range(1, 6).Select(id => new EvaluationInfo { target_id = id, evaluation = 80 })];
    }

    static void DispatchResponse(Type endpointType, object response, WorkspaceSmokeSession? ui = null)
        => Dispatch(AnalyzerKind.Response, GameEndpointCatalog.ByEndpointType[endpointType], MessagePackSerializer.Serialize(response.GetType(), response), ui);

    static void Dispatch(AnalyzerKind kind, GameEndpointDescriptor endpoint, byte[] payload, WorkspaceSmokeSession? ui = null)
    {
        var registrations = PluginManager.SnapshotAnalyzerRegistrations(kind, endpoint.EndpointType);
        Require(registrations.Length != 0, $"No production registrations matched {kind} {endpoint.Path}.");
        var context = new AnalyzerDispatchContext(endpoint, payload, Headers);
        for (var index = 0; index < registrations.Length; index++)
        {
            var registration = registrations[index];
            using var callback = PluginManager.TryEnterPluginCallback(registration.Plugin);
            Require(callback is not null && !registration.IsFaulted, $"ZIP analyzer is unavailable: {registration.Source}.");
            using var owner = HotkeyManager.RegisterScope(registration.Plugin);
            var checkStatusWorkspace = ui is not null && PluginManager.InternalName(registration.Plugin) == "SendGameStatusPlugin";
            var workspace = Workspace.Current;
            var screen = checkStatusWorkspace ? ui!.CaptureScreen(200, 80) : null;
            registration.Handler(context).GetAwaiter().GetResult();
            if (checkStatusWorkspace)
                Require(ReferenceEquals(workspace, Workspace.Current) && screen == ui!.CaptureScreen(200, 80),
                    "SendGameStatus ZIP callback changed the workspace or framebuffer.");
        }
    }

    static void AssertPanel(WorkspaceSmokeSession ui, string title, params string[] expected)
    {
        Workspace.Create(title).SwitchTo();
        var screen = ui.CaptureScreen(200, 80);
        foreach (var token in expected)
            Require(screen.Contains(token, StringComparison.Ordinal), $"{title} panel omitted '{token}':\n{screen}");
    }

    // Inspection only: handlers are always entered through the production registration snapshot.
    static object Member(object instance, string name)
    {
        var type = instance.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return (type.GetProperty(name, flags)?.GetValue(instance) ?? type.GetField(name, flags)?.GetValue(instance))
            ?? throw new InvalidOperationException($"Missing observable state {type.FullName}.{name}.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
static async Task InitializeSmokeDatabase(
    IEnumerable<Story>? events = null,
    IEnumerable<UmamusumeResponseAnalyzer.Entities.SkillData>? skills = null)
{
    WriteBrotliJson(Database.EVENT_NAME_FILEPATH, (events ?? []).ToList());
    WriteBrotliJson(
        Database.NAMES_FILEPATH,
        new List<BaseName>
        {
            new SupportCardName(10001, "fixture support 1", "S1", 101, 1001),
            new SupportCardName(10002, "fixture support 2", "S2", 105, 1002),
            new SupportCardName(10003, "fixture support 3", "S3", 102, 1003),
            new SupportCardName(10004, "fixture support 4", "S4", 103, 1004),
            new SupportCardName(10005, "fixture support 5", "S5", 106, 1005),
            new SupportCardName(10006, "fixture support 6", "S6", 0, 1006),
        },
        new() { TypeNameHandling = TypeNameHandling.All });
    WriteBrotliJson(Database.SKILLS_FILEPATH, (skills ?? []).ToList());
    WriteBrotliJson(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, new List<SkillUpgradeSpeciality>());
    WriteBrotliJson(Database.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>());
    WriteBrotliJson(Database.FACTOR_IDS_FILEPATH, new Dictionary<int, string>());
    WriteBrotliJson(Database.SADDLE_IDS_FILEPATH, Array.Empty<int>());
    WriteBrotliJson(Database.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());
    if (await Database.Initialize() != DatabaseAvailability.Ready)
        throw new InvalidOperationException("Smoke database fixture did not load atomically.");
}

static void WriteBrotliJson<T>(string path, T value, JsonSerializerSettings? settings = null)
{
    using var file = File.Create(path);
    using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
    using var writer = new StreamWriter(brotli, Encoding.UTF8);
    using var json = new JsonTextWriter(writer);
    (settings is null
        ? Newtonsoft.Json.JsonSerializer.CreateDefault()
        : Newtonsoft.Json.JsonSerializer.CreateDefault(settings)).Serialize(json, value);
}

}
