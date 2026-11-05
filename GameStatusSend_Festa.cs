using EventLoggerPlugin;
using FestaScenarioAnalyzer;
using Gallop;

namespace SendGameStatusPlugin
{
    /// <summary>一个演目（训练位）的协议状态，字段值与 <c>act_info_array</c> 一一对应。</summary>
    public class FestaActFrame
    {
        public int actType;         // 1–5，与训练位同序
        public int gauge;           // act_gauge
        public int level;           // level（1 基）
        public int needGauge;       // need_act_gauge
        public int adviceId;        // 基础教诲 id（101–501），0 = 未分配
        public int fusionId;        // 转成 id（601–1001），0 = 未转成
        public int outingGainLevel; // friend_outing_gain_act_level
    }

    /// <summary>発表会（检查点）结果。<c>resultType</c> 原样转发，0/1 语义未核，换算留给 matsuri。</summary>
    public class FestaCheckPointFrame
    {
        public int scheduleId;
        public int resultType;
    }

    /// <summary>
    /// 担当名单条目。<c>targetId</c> 与 <c>distribution</c> 同一编号空间（卡位 1 基）。
    /// <c>origin</c> 取 <c>card / friend / guest / yayoi / reporter</c>。
    /// </summary>
    public class FestaPersonFrame
    {
        public int targetId;
        public int charaId;
        public int actType;
        public string origin = "";
    }

    /// <summary>
    /// 骏大祭（scenario 15）剧本段。段名固定 <c>festa</c>；值尽量原样转发协议，
    /// 换算（click → level、result_type 映射等）留给 matsuri。
    /// </summary>
    public class FestaFrame
    {
        public FestaActFrame[] acts = [];
        public int maxActLevel;
        public int campRemain;          // summer_camp_remain_turn_num
        public int assistSupportLeft;   // 状态 47 存在时取 training_bonus_remain_turn_num，否则 0
        public bool fatigueRecovery;    // 状态 45
        public FestaCheckPointFrame[] checkPoints = [];
        /// <summary>协议总等级（含発表会 +1），5 项按训练位排序。</summary>
        public int[] trainLevelTotal = new int[5];
        /// <summary>追加稽古标记，逐训练位；engine 侧取任一为真。</summary>
        public bool[] kensan = new bool[5];
        public FestaPersonFrame[] persons = [];
        /// <summary>每个训练位的参与者 target_id（原始协议值，1 基卡位 / 102 / 103 / ≥1000）。</summary>
        public int[][] distribution = new int[5][];
    }

    public class GameStatusSend_Festa : IHasBaseGame
    {
        /// <summary>骏大祭的友人卡：SSR 吾妻 / R 吾妻。</summary>
        static readonly int[] FriendCardIds = [30323, 10149];

        public GameStatusSend_Base<PersonBase> baseGame { get; set; }
        public FestaFrame? festa;

        internal GameStatusSend_Festa(SingleModeFestaCheckEventResponse @event)
        {
            var round = EventLogger.Current;
            var chara = @event.data.chara_info;

            baseGame = new GameStatusSend_Base<PersonBase>(@event, round);
            baseGame.scenarioId = (int)ScenarioType.Festa;

            // 快照必须与当前帧同源：演出位、教诲与担当都来自同一份 festa_data_set，
            // 错帧会把上一回合的演目状态拼进来，直接判成不发。
            var snapshot = FestaScenarioState.Snapshot();
            if (snapshot is null
                || snapshot.CharaId != chara.single_mode_chara_id
                || snapshot.Turn != chara.turn)
            {
                baseGame.islegal = false;
                return;
            }

            ApplyTrainLevelCounts(round);
            ApplyPersonDistribution(snapshot);
            ApplyFriendStage(chara);

            festa = BuildFrame(snapshot, chara);
        }

        /// <summary>
        /// 骏大祭的 <c>trainLevelCount</c> 直接取 EventLogger 累计的成功普通训练次数。
        /// 通用段按 <c>4×(level−1)</c> 折算会让「等级内余数」恒为 0，AI 会误判「离下一级还差整整 4 次」。
        /// 稽古（5101–5105）与训练失败都不进普通次数，口径见 EventLogger <c>RecordFestaCommand</c>。
        /// </summary>
        void ApplyTrainLevelCounts(EventLoggerRoundSnapshot round)
        {
            var counts = round.NormalTrainingCounts;
            for (var i = 0; i < 5; i++)
                baseGame.trainLevelCount[i] = i < counts.Length ? counts[i] : 0;
        }

        /// <summary>
        /// 通用段的 <c>personDistribution</c> 依赖 <c>GameGlobal.ToTrainIndex</c>，而该表没有
        /// 稽古指令 5101–5105，骏大祭会整条丢掉训练位；<c>lockedTrainingId</c> 又按
        /// <c>command_id % 10</c> 计算，对 5101 之类的 id 无意义。故一律以快照为准重建，
        /// 锁定位置写 -1（本剧本不做训练位锁定）。
        /// </summary>
        void ApplyPersonDistribution(FestaTurn snapshot)
        {
            for (var i = 0; i < 5; i++)
                for (var j = 0; j < 5; j++)
                    baseGame.personDistribution[i, j] = -1;

            foreach (var training in snapshot.Training)
            {
                if (training.Index is < 0 or > 4)
                    continue;
                var slot = 0;
                foreach (var partner in training.Partners)
                {
                    if (slot >= 5)
                        break;
                    baseGame.personDistribution[training.Index, slot] = ToUmaAiPersonId(partner.TargetId);
                    slot += 1;
                }
            }

            baseGame.lockedTrainingId = -1;
        }

        /// <summary>协议 target_id → baseGame 的人头编号（卡位 0–5、理事长 6、记者 7、神驹 8）。</summary>
        static int ToUmaAiPersonId(int targetId) => targetId switch
        {
            102 => 6,
            103 => 7,
            >= 1000 => 8,
            _ => targetId - 1
        };

        /// <summary>
        /// 通用段靠 <c>CookFriendAtTrain</c> 历史判断「点过友人但还没解锁出行」，该历史只在大丰食祭
        /// 记录，骏大祭恒为空，导致 <c>friend_stage</c> 只会是 0 或 2。
        /// 这里用协议自带的 <c>story_step</c> 兜住 stage 1（已点击、出行未解锁）。
        /// </summary>
        void ApplyFriendStage(SingleModeChara chara)
        {
            if (baseGame.friend_stage == 2)
                return;

            for (var i = 0; i < baseGame.cardId.Length; i++)
            {
                if (!FriendCardIds.Contains(baseGame.cardId[i] / 10))
                    continue;

                var info = chara.evaluation_info_array?.FirstOrDefault(x => x.target_id == i + 1);
                if (info is not null && info.is_outing != 1 && info.story_step > 0)
                    baseGame.friend_stage = 1;
                return;
            }
        }

        static FestaFrame BuildFrame(FestaTurn snapshot, SingleModeChara chara)
        {
            var supportCards = (chara.support_card_array ?? [])
                .GroupBy(x => x.position)
                .ToDictionary(x => x.Key, x => x.First().support_card_id);

            var trainLevelTotal = TrainLevelsFromProtocol(chara);
            var kensan = new bool[5];
            var distribution = new int[5][];
            for (var i = 0; i < 5; i++)
                distribution[i] = [];

            foreach (var training in snapshot.Training)
            {
                if (training.Index is < 0 or > 4)
                    continue;
                trainLevelTotal[training.Index] = training.Level;
                kensan[training.Index] = training.UpperTraining;
                distribution[training.Index] = [.. training.Partners.Select(x => x.TargetId)];
            }

            return new FestaFrame
            {
                acts =
                [
                    .. snapshot.Acts.Select(act => new FestaActFrame
                    {
                        actType = act.Type,
                        gauge = act.Gauge,
                        level = act.Level,
                        needGauge = act.NeedGauge,
                        adviceId = act.AdviceId,
                        fusionId = act.FusionId,
                        outingGainLevel = act.OutingLevel
                    })
                ],
                maxActLevel = snapshot.MaxActLevel,
                campRemain = snapshot.CampRemaining,
                // 状态 47 是「助力支撑可用」的开关，剩余次数挂在 home_info 上；
                // 状态消失时次数无意义，写 0。
                assistSupportLeft = snapshot.AssistSupportActive ? snapshot.AssistRemain : 0,
                fatigueRecovery = snapshot.FatigueRecovery,
                checkPoints =
                [
                    .. snapshot.Checkpoints.Select(x => new FestaCheckPointFrame
                    {
                        scheduleId = x.Id,
                        resultType = x.ResultType
                    })
                ],
                trainLevelTotal = trainLevelTotal,
                kensan = kensan,
                persons =
                [
                    .. snapshot.Members.Select(member => new FestaPersonFrame
                    {
                        targetId = member.TargetId,
                        charaId = member.CharaId,
                        actType = member.ActType,
                        origin = OriginOf(member.TargetId, supportCards)
                    })
                ],
                distribution = distribution
            };
        }

        /// <summary>
        /// 协议总等级（含発表会 +1）。快照没有训练位明细时（load / start 不算可训练帧）
        /// 仍要给出真值，否则 matsuri 会把 <c>train_level_bonus</c> 算成负数。
        /// 演目稽古位可能挂在 5101–5105 上，故两种 id 都认。
        /// </summary>
        static int[] TrainLevelsFromProtocol(SingleModeChara chara)
        {
            var result = new int[5];
            foreach (var info in chara.training_level_info_array ?? [])
            {
                var index = info.command_id is >= 5101 and <= 5105
                    ? info.command_id - 5101
                    : GameGlobal.ToTrainIndex.GetValueOrDefault(info.command_id, -1);
                if (index is >= 0 and <= 4)
                    result[index] = info.level;
            }
            return result;
        }

        static string OriginOf(int targetId, Dictionary<int, int> supportCards)
        {
            if (targetId is >= 1 and <= 6)
            {
                return supportCards.TryGetValue(targetId, out var cardId) && FriendCardIds.Contains(cardId)
                    ? "friend"
                    : "card";
            }

            return targetId switch
            {
                102 => "yayoi",
                103 => "reporter",
                _ => "guest"
            };
        }

        public void doSend()
        {
            if (!baseGame.islegal)
            {
                return;
            }
            GameStatusOutput.WriteScenarioData(this, baseGame.turn);
        }
    }
}
