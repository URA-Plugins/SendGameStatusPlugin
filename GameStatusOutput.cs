using Newtonsoft.Json;

namespace SendGameStatusPlugin;

internal static class GameStatusOutput
{
    static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    static readonly object WriteGate = new();

    // 同一 (charaId, turn) 的重复调用计数器；按 (charaId, turn) 复合 key 累计 N。
    // 进程内有效，不跨插件重载保留。
    static readonly Dictionary<(long charaId, int turn), int> TurnSequence = [];

    public static string PluginDataDirectory { get; private set; } = Path.Combine("PluginData", "SendGameStatusPlugin");

    public static void Configure(string pluginDataDirectory)
        => PluginDataDirectory = pluginDataDirectory;

    public static void WritePluginData(object payload, int turn)
    {
        lock (WriteGate)
        {
            Write(PluginDataDirectory, payload, turn);
        }
    }

    public static void WriteScenarioData(object payload, int turn)
        => WritePluginData(payload, turn);

    static void Write(string directory, object payload, int turn)
    {
        Directory.CreateDirectory(directory);
        var charaId = GetSingleModeCharaId(payload);
        var sequence = NextSequence(charaId, turn);

        var fileName = sequence > 1
            ? $"game{charaId}_turn{turn}_{sequence}.json"
            : $"game{charaId}_turn{turn}.json";
        var turnPath = Path.Combine(directory, fileName);
        var currentTurnPath = Path.Combine(directory, "thisTurn.json");

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

    /// <summary>
    /// 同一 (charaId, turn) 的下一次序号；首调返回 1。
    /// </summary>
    static int NextSequence(long charaId, int turn)
    {
        var key = (charaId, turn);
        var n = TurnSequence.TryGetValue(key, out var current) ? current + 1 : 1;
        TurnSequence[key] = n;
        return n;
    }

    /// <summary>
    /// 从 payload 的 <c>baseGame.single_mode_chara_id</c> 取育成 ID。
    /// 要求 payload 实现 IHasBaseGame；不实现时返回 0。
    /// </summary>
    static long GetSingleModeCharaId(object payload)
        => payload is IHasBaseGame { baseGame: { } baseGame }
            ? baseGame.single_mode_chara_id
            : 0;

    static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, path, true);
    }
}
