using Newtonsoft.Json;
using UmamusumeResponseAnalyzer.LiveDisplay;

namespace SendGameStatusPlugin;

internal static class GameStatusOutput
{
    static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string PluginDataDirectory { get; private set; } = Path.Combine("PluginData", "SendGameStatusPlugin");

    public static void Configure(string pluginDataDirectory)
    {
        PluginDataDirectory = pluginDataDirectory;
    }

    public static void WritePluginData(object payload, int turn, string? scenarioDirectory = null, bool logSuccess = false)
    {
        var directory = scenarioDirectory is null
            ? PluginDataDirectory
            : Path.Combine(PluginDataDirectory, scenarioDirectory);
        Write(directory, payload, turn, logSuccess);
    }

    public static void WriteScenarioData(object payload, int turn, bool logSuccess = false)
        => WritePluginData(payload, turn, payload.GetType().Name, logSuccess);

    public static void LogInfo(string text)
        => LiveDisplayConsole.Log("SendGameStatusPlugin", text);

    public static void LogWarning(string text)
        => LiveDisplayConsole.Log("SendGameStatusPlugin", text, LiveDisplaySeverity.Warning);

    public static void LogError(string text)
        => LiveDisplayConsole.Log("SendGameStatusPlugin", text, LiveDisplaySeverity.Error);

    static void Write(string directory, object payload, int turn, bool logSuccess = false)
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
                File.WriteAllText(currentTurnPath, json);
                File.WriteAllText(turnPath, json);
                if (logSuccess)
                    LogInfo("回合已保存，等待AI计算");
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                LogWarning($"写入 {currentTurnPath} 失败，0.5秒后重试: {ex.Message}");
                Thread.Sleep(500);
            }
        }

        throw new IOException($"写入 {currentTurnPath} 失败，已重试 10 次。", lastException);
    }
}
