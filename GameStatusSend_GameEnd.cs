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
            inheritGains = round.InheritStats.ToArray()
        };
    }

    public void doSend() => GameStatusOutput.WriteFinalScore(payload);
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
