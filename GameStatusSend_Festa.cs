using EventLoggerPlugin;
using FestaScenarioAnalyzer;
using Gallop;
using UmamusumeResponseAnalyzer;

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
    /// 担当名单条目：谁属于哪个演目。<c>targetId</c> 与 <c>baseGame.personDistribution</c>
    /// <b>同一编号空间</b>（卡位 1–6 → 0–5、理事长 102 → 6、记者 103 → 7、NPC ≥1000 用原始 id），
    /// 已做 BaseGame 的 0 基换算，可以直接拿分布里的人头来查。
    /// 只发有演目归属的成员（<c>actType &gt; 0</c>）：<c>actType == 0</c> 的是不参与演目的 guest，
    /// 换算后 id 还会落进 100–114 与真实 charaId 混淆，故不发。
    /// 演目归属只有这里能表达，不重复进 <c>baseGame</c>。
    /// <c>origin</c> 取 <c>card / friend / guest / yayoi / reporter</c>。
    /// </summary>
    public class FestaPersonFrame
    {
        public int targetId;
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
        /// <summary>
        /// 助力支撑是否生效：状态 47 存在时为 1，否则 0。<b>只表示有无，不是剩余次数</b>。
        /// 协议不给次数（<c>home_info.training_bonus_remain_turn_num</c> 在骏大祭恒 0），
        /// 插件侧也不维护计数器——需要次数时由消费端按自己的规则累计。
        /// </summary>
        public int assistSupportLeft;
        public bool fatigueRecovery;    // 状态 45
        public FestaCheckPointFrame[] checkPoints = [];
        /// <summary>
        /// 5 个训练位的等级，按 速/耐/力/根/智 排序。<c>0</c> 表示<b>本帧拿不到可信值</b>
        /// （合宿期只有稽古项、事件帧协议不回等级），不是「等级 0」；消费端应自行判未知。
        /// </summary>
        public int[] trainLevelTotal = new int[5];

        /// <summary>本帧哪些位拿到了快照里的基础训练项等级（内部用，不序列化）。</summary>
        internal bool[] trustedLevel = new bool[5];
        /// <summary>追加稽古标记，逐训练位；engine 侧取任一为真。</summary>
        public bool[] kensan = new bool[5];
        /// <summary>
        /// 担当名单：人物 id、来源与所属演目。训练位参与者已并入
        /// <c>baseGame.personDistribution</c>，此处不重复发分布，只补「谁属于哪个演目」。
        /// </summary>
        public FestaPersonFrame[] persons = [];
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
            ApplyPersonCharaIds(chara);
            ApplyFriendStage(chara);
            ApplyTraining(@event.data.home_info, @event.data.festa_data_set);

            festa = BuildFrame(snapshot, chara);
        }

        /// <summary>
        /// 填充通用段的 <c>training</c>：逐条可训练指令给出「下层 / 上层」两块 7 项增量。
        /// 直接遍历协议而非按 5 个训练位展开，这样稽古（5101–5105）、转成（601–605）
        /// 以及以后剧本新增的训练项都能原样落进来，映射由 AI 端自己做。
        /// </summary>
        void ApplyTraining(SingleModeHomeInfo? home, SingleModeFestaDataSet? data)
        {
            var result = new Dictionary<int, TrainingFrame>();
            foreach (var command in home?.command_info_array ?? [])
            {
                // command_type 1 才是训练；is_enable 为 0 的位本回合打不了，不出现。
                if (command.command_type != 1 || command.is_enable != 1)
                    continue;

                if (!result.TryGetValue(command.command_id, out var frame))
                {
                    frame = new TrainingFrame { failureRate = command.failure_rate };
                    result[command.command_id] = frame;
                }

                Accumulate(frame.lower, command.params_inc_dec_info_array);
                foreach (var scenario in data?.command_info_array ?? [])
                    if (scenario.command_type == 1 && scenario.command_id == command.command_id)
                        Accumulate(frame.upper, scenario.params_inc_dec_info_array);
            }

            // 空字典也要发：不可训练回合（合宿 / 比赛）靠它区分「没有训练」和「字段缺失」。
            baseGame.training = result;
        }

        /// <summary>
        /// 累加到 7 项数组：target_type 1–5 → 速/耐/力/根/智，30 → 技能点，10 → 体力。
        /// 顺序与剧本 <c>training.normal</c> 的基础值一致。
        /// </summary>
        static void Accumulate(int[] target, SingleModeParamsIncDecInfo[]? values)
        {
            foreach (var value in values ?? [])
            {
                switch (value.target_type)
                {
                    case >= 1 and <= 5:
                        target[value.target_type - 1] += value.value;
                        break;
                    case 30:
                        target[5] += value.value;
                        break;
                    case 10:
                        target[6] += value.value;
                        break;
                }
            }
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
        /// 每个训练位最多 5 人，超出截断（骏大祭实际也不会超过 5 人）。
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

        /// <summary>
        /// 通用段 <c>persons</c> 只有 6 条（支援卡位 1–6），原实现把 <c>charaId</c> 一律留 0，
        /// 导致按 targetId 反查不到角色。这里补真实 chara id；NPC 的 targetId 本身就是 chara id。
        /// </summary>
        void ApplyPersonCharaIds(SingleModeChara chara)
        {
            foreach (var card in chara.support_card_array ?? [])
            {
                var index = card.position - 1;
                if (index is < 0 or >= 6)
                    continue;
                if (Database.Names.TryGetSupportCard(card.support_card_id, out var info))
                    baseGame.persons[index].charaId = info.CharaId;
            }
        }

        /// <summary>
        /// 协议 target_id → baseGame 人头编号。<b>只有 NPC 用原始 ID</b>：
        /// 支援卡位保持 BaseGame 的 0 基约定（1–6 → 0–5），理事长 102 → 6、记者 103 → 7；
        /// NPC（≥1000）直接写 chara id，不再压成 8，否则身份全丢。
        /// </summary>
        static int ToUmaAiPersonId(int targetId) => targetId switch
        {
            102 => 6,
            103 => 7,
            >= 1000 => targetId,
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
            var trustedLevel = new bool[5];
            var kensan = new bool[5];

            foreach (var training in snapshot.Training)
            {
                if (training.Index is < 0 or > 4)
                    continue;
                kensan[training.Index] = training.UpperTraining;

                // 稽古项的 level 恒 1，是稽古自身等级，与训练位等级无关，不参与。
                if (training.CommandId is >= 5101 and <= 5105)
                    continue;
                trainLevelTotal[training.Index] = training.Level;
                trustedLevel[training.Index] = true;
            }

            return new FestaFrame
            {
                trustedLevel = trustedLevel,
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
                assistSupportLeft = snapshot.AssistSupportActive ? 1 : 0,
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
                    .. snapshot.Members
                        .Where(member => member.ActType > 0)
                        .Select(member => new FestaPersonFrame
                        {
                            targetId = ToUmaAiPersonId(member.TargetId),
                            actType = member.ActType,
                            origin = OriginOf(member.TargetId, supportCards)
                        })
                ]
            };
        }

        /// <summary>
        /// 逐训练位取协议等级，<b>只认基础训练项</b>（101–106 等）。稽古 5101–5105 的 level
        /// 恒为 1、是稽古自身等级，与训练位等级不是同一量纲，直接跳过，绝不拿来填位。
        /// 拿不到基础项的位保持 <c>0</c>（本帧无有效值），由消费端自行判未知，不做任何推算。
        /// </summary>
        static int[] TrainLevelsFromProtocol(SingleModeChara chara)
        {
            var result = new int[5];
            foreach (var info in chara.training_level_info_array ?? [])
            {
                if (info.command_id is >= 5101 and <= 5105)
                    continue;

                var index = GameGlobal.ToTrainIndex.GetValueOrDefault(info.command_id, -1);
                if (index is >= 0 and <= 4)
                    result[index] = info.level;
            }
            return result;
        }

        // 训练位等级基准：只有 load / command 帧的协议值可信（load 是整局快照，数据更全）。
        // 合宿与终盘只能稽古，协议不回基础项，这些帧缺失的位用最近一次观测值补齐；
        // 事件 / 结算帧协议把基础项填成默认 1、不可信，仍发 0。
        static readonly object LevelBaselineGate = new();
        static readonly int[] LevelBaseline = new int[5];
        static long levelBaselineCharaId;

        internal void ApplyLevelBaseline(string? source)
        {
            if (festa is null)
                return;

            var authoritative = source is "command" or "load";
            lock (LevelBaselineGate)
            {
                if (levelBaselineCharaId != baseGame.single_mode_chara_id)
                {
                    levelBaselineCharaId = baseGame.single_mode_chara_id;
                    Array.Clear(LevelBaseline);
                }

                for (var i = 0; i < 5; i++)
                {
                    if (!authoritative)
                    {
                        festa.trainLevelTotal[i] = 0;
                        continue;
                    }

                    // 只有快照里的基础训练项等级可信；协议原值在不可训练回合也会被填成 1，
                    // 不能拿来更新基准，缺可信值时一律用基准。
                    if (festa.trustedLevel[i])
                        LevelBaseline[i] = festa.trainLevelTotal[i];
                    else
                        festa.trainLevelTotal[i] = LevelBaseline[i];
                }
            }
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
