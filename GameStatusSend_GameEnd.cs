using System.Reflection;
using System.Runtime.CompilerServices;
using EventLoggerPlugin;
using Gallop;

namespace SendGameStatusPlugin;

/// <summary>
/// 终局帧 payload：育成结束·点技能前的扁平结构（无 baseGame 外壳）。
/// 字段名与 umaai 侧 FinalScorePayload 契约一一对应；<c>single_mode_chara_id</c> /
/// <c>playing_state</c> 保持 snake_case，与 thisTurn.json 的 baseGame 段同构。
/// </summary>
public class GameEndPayload
{
    public int scenarioId;              // 剧本 ID（原值，不限于拉面）
    public int single_mode_chara_id;    // 育成 ID（归局键）
    public int umaId;
    public int umaStar;
    public int turn;                    // 0 基，与 thisTurn.json 同口径
    public int state;                   // chara_info.state（2/3 = 育成结束态）
    public int playing_state;
    public int[] fiveStatus;            // 已 ReviseOver1200
    public int[] fiveStatusLimit;       // 已 ReviseOver1200
    public int skillPt;                 // 点技能前的剩余技能点
    public int totalHints;              // 总 Hint 等级（skill_tips_array 各项 level 之和）
    public int skillPtSpent;            // 本局累计花掉的技能点（EventLogger 统计）
    public int[] inheritGains;          // 两次继承的属性增量（按继承顺序）

    // 玩家实际动作（整局一次落盘，与终局数据同帧）。契约见 README：
    // 动作段结构版本；消费端靠它区分带采集的新帧与历史老帧，当前为 1。
    public int actionSchemaVersion;
    // 数组下标即到达顺序；元素为 turn/endpoint + 请求 DTO 字段按协议原名展平。
    public List<Dictionary<string, object?>> actions = [];
}

/// <summary>
/// 终局帧发送：只读 <see cref="SingleModeChara"/>，不复用
/// <see cref="GameStatusSend_Base{T}"/>——后者的构造函数硬依赖
/// <c>home_info.command_info_array</c> / <c>support_card_array</c> /
/// <c>evaluation_info_array</c>，终局与技能购买屏都不带这些字段。
/// </summary>
public class GameStatusSend_GameEnd
{
    public GameEndPayload payload;

    public GameStatusSend_GameEnd(SingleModeChara charaInfo)
    {
        var round = EventLogger.Current;
        payload = new GameEndPayload
        {
            scenarioId = charaInfo.scenario_id,
            single_mode_chara_id = charaInfo.single_mode_chara_id,
            umaId = charaInfo.card_id,
            umaStar = charaInfo.rarity,
            turn = charaInfo.turn - 1,
            state = charaInfo.state,
            playing_state = charaInfo.playing_state,
            fiveStatus =
            [
                ScoreUtils.ReviseOver1200(charaInfo.speed),
                ScoreUtils.ReviseOver1200(charaInfo.stamina),
                ScoreUtils.ReviseOver1200(charaInfo.power),
                ScoreUtils.ReviseOver1200(charaInfo.guts),
                ScoreUtils.ReviseOver1200(charaInfo.wiz)
            ],
            fiveStatusLimit =
            [
                ScoreUtils.ReviseOver1200(charaInfo.max_speed),
                ScoreUtils.ReviseOver1200(charaInfo.max_stamina),
                ScoreUtils.ReviseOver1200(charaInfo.max_power),
                ScoreUtils.ReviseOver1200(charaInfo.max_guts),
                ScoreUtils.ReviseOver1200(charaInfo.max_wiz)
            ],
            skillPt = charaInfo.skill_point,
            totalHints = (charaInfo.skill_tips_array ?? []).Sum(tip => tip.level),
            skillPtSpent = round.SkillPtSpent,
            inheritGains = round.InheritStats.ToArray(),
            actionSchemaVersion = 1,
            actions = ActionLog.TakeSnapshot()
        };
    }

    public void doSend() => GameStatusOutput.WriteFinalScore(payload, payload.single_mode_chara_id);
}

public partial class SendGameStatusPlugin
{
    /// <summary>
    /// 已写过终局帧的育成 ID（进程内）。玩家可能先买一部分技能再回到该画面，
    /// 后续帧的 skill_point 更小，覆盖会低估 SP——故同局只写第一份。
    /// </summary>
    static readonly HashSet<int> FinalScoreWritten = [];
    static readonly object FinalScoreGate = new();

    /// <summary>
    /// 终局帧判据：育成结束态（<c>state</c> 为 2 或 3）且无待处理事件。
    /// 该帧已含全部结局事件与末回合比赛奖励。
    /// </summary>
    ValueTask AnalyzeGameEnd(SingleModeChara? charaInfo, SingleModeEventInfo[]? uncheckedEvents)
    {
        // 响应帧带育成 ID，用来给内存动作表定归属；换局时清掉上一局未落盘的动作。
        // 必须先切局再观察：否则中途 load 的新局会在清空时把刚写入的选项数一并丢掉。
        if (charaInfo is not null)
            ActionLog.ObserveGame(charaInfo.single_mode_chara_id);

        // 待处理事件列表里的 choice_array 是选项数的唯一可靠来源，顺手喂给动作表。
        ActionLog.ObserveEvents(uncheckedEvents);

        if (charaInfo is null || charaInfo.state is not (2 or 3)
            || (uncheckedEvents?.Length ?? 0) != 0)
            return ValueTask.CompletedTask;

        lock (FinalScoreGate)
        {
            if (FinalScoreWritten.Contains(charaInfo.single_mode_chara_id))
                return ValueTask.CompletedTask;

            WriteGameEnd(charaInfo);
            FinalScoreWritten.Add(charaInfo.single_mode_chara_id);
        }

        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void WriteGameEnd(SingleModeChara charaInfo)
        => new GameStatusSend_GameEnd(charaInfo).doSend();
}

/// <summary>
/// 玩家实际动作采集：进程内按到达顺序累积，只有正常终局才随终局帧一次性落盘。
/// 全程不写中间文件，也不进 <c>thisTurn.json</c> 决策链路。
/// </summary>
internal static class ActionLog
{
    static readonly object Gate = new();
    static readonly List<Dictionary<string, object?>> Actions = [];
    static readonly HashSet<Guid> SeenRequestIds = [];
    // event_id → 整局见过的最大选项数（来自各响应的 unchecked_event_array）。
    // 取最大值而非最后一次：某个 event_id 只要见过一次多选项，就永不判成自动事件。
    static readonly Dictionary<int, int> MaxChoiceCount = [];
    static long CurrentGameId;

    /// <summary>
    /// 由响应帧观察当前育成 ID。首次见到局号只建立归属、不清空（动作可能先于响应帧到达）；
    /// 之后局号变化才清空上一局未落盘的动作。
    /// </summary>
    public static void ObserveGame(long charaId)
    {
        if (charaId <= 0)
            return;

        lock (Gate)
        {
            if (CurrentGameId == charaId)
                return;
            if (CurrentGameId != 0)
            {
                Actions.Clear();
                SeenRequestIds.Clear();
                MaxChoiceCount.Clear();
            }
            CurrentGameId = charaId;
        }
    }

    /// <summary>
    /// 从响应的 <c>unchecked_event_array</c> 收集每个 <c>event_id</c> 的选项数。
    /// 请求侧只有 <c>event_id / choice_number</c>，无法区分「玩家选了第 0 项」与
    /// 「只有一个选项、客户端自动确认」，故必须靠响应侧补信息。
    /// <c>choice_array</c> 为 null（协议没给）时视为未知、不写表，落盘时保守保留该事件。
    /// </summary>
    public static void ObserveEvents(SingleModeEventInfo[]? events)
    {
        if (events is null)
            return;

        lock (Gate)
        {
            foreach (var info in events)
            {
                if (info is null || info.event_contents_info?.choice_array is not { } choices)
                    continue;

                var count = choices.Length;
                if (!MaxChoiceCount.TryGetValue(info.event_id, out var known) || count > known)
                    MaxChoiceCount[info.event_id] = count;
            }
        }
    }

    /// <summary>
    /// 记录一条请求动作。<paramref name="common"/> 为各剧本的 <c>xxx_request_common</c>，
    /// 其 public 字段按协议原名展平；<c>current_turn</c> 换算成 0 基 <c>turn</c> 后不重复落盘。
    /// RequestId 只作进程内去重键，不写入 JSON。
    /// </summary>
    public static void Record(string endpoint, object? common, Guid requestId)
    {
        if (common is null)
            return;

        lock (Gate)
        {
            if (!SeenRequestIds.Add(requestId))
                return;

            var fields = new Dictionary<string, object?>();
            foreach (var field in common.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                fields[field.Name] = field.GetValue(common);

            var currentTurn = fields.TryGetValue("current_turn", out var value) && value is int turn
                ? turn
                : 0;
            fields.Remove("current_turn");

            var action = new Dictionary<string, object?>
            {
                ["turn"] = currentTurn - 1,
                ["endpoint"] = endpoint
            };
            foreach (var pair in fields)
                action[pair.Key] = pair.Value;
            Actions.Add(action);
        }
    }

    /// <summary>
    /// 终局帧取快照；取走即清空，避免结算画面的后续请求残留到下一局。
    /// 落盘前丢弃「无玩家可选」的自动事件（见 <see cref="IsAutomaticEvent"/>）。
    /// </summary>
    public static List<Dictionary<string, object?>> TakeSnapshot()
    {
        lock (Gate)
        {
            var snapshot = new List<Dictionary<string, object?>>(Actions.Count);
            foreach (var action in Actions)
                if (!IsAutomaticEvent(action))
                    snapshot.Add(action);

            Actions.Clear();
            SeenRequestIds.Clear();
            MaxChoiceCount.Clear();
            return snapshot;
        }
    }

    /// <summary>
    /// 「单选项自动事件」判据：整局见过的最大选项数 ≤ 1，客户端无需玩家选择即发
    /// <c>choice_number=0</c>。查不到选项数的 <c>event_id</c> 一律保留——
    /// 宁可漏过滤，也不误删真实选择（多选项事件里玩家选第 0 项是合法动作）。
    /// </summary>
    static bool IsAutomaticEvent(Dictionary<string, object?> action)
        => action.TryGetValue("endpoint", out var endpoint)
           && endpoint is "check_event"
           && action.TryGetValue("event_id", out var value)
           && value is int eventId
           && MaxChoiceCount.TryGetValue(eventId, out var count)
           && count <= 1;
}
