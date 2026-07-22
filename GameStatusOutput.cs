using Newtonsoft.Json;

namespace SendGameStatusPlugin;

internal static class GameStatusOutput
{
    static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    static readonly object WriteGate = new();

    public static string PluginDataDirectory { get; private set; } = Path.Combine("PluginData", "SendGameStatusPlugin");

    public static void Configure(string pluginDataDirectory)
        => PluginDataDirectory = pluginDataDirectory;

    public static void WritePluginData(object payload, int turn, string? scenarioDirectory = null)
    {
        var directory = scenarioDirectory is null
            ? PluginDataDirectory
            : Path.Combine(PluginDataDirectory, scenarioDirectory);
        lock (WriteGate)
        {
            Write(directory, payload, turn);
        }
    }

    public static void WriteScenarioData(object payload, int turn)
        => WritePluginData(payload, turn, payload.GetType().Name);

    static void Write(string directory, object payload, int turn)
    {
        Directory.CreateDirectory(directory);
        var currentTurnPath = Path.Combine(directory, "thisTurn.json");
        var turnPath = Path.Combine(directory, $"turn{turn}.json");

        Exception? lastException = null;
        for (var tried = 0; tried < 10; tried++)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload, Formatting.Indented, Settings);
                WriteAtomically(turnPath, json);
                WriteAtomically(currentTurnPath, json);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Thread.Sleep(500);
            }
        }

        throw new IOException($"写入 {currentTurnPath} 失败，已重试 10 次。", lastException);
    }

    static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, path, true);
    }
}
