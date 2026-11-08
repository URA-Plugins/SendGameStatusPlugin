using EventLoggerPlugin;
using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;

namespace SendGameStatusPlugin
{
    /// <summary>
    /// 传递给AI的人头信息的基本接口（测试）
    /// </summary>
    public class PersonBase
    {
        //0代表未加载（例如前两个回合的npc），1代表友人（R或SSR都行），2代表普通支援卡，3代表npc人头，4理事长，5记者，6不带卡的佐岳。暂不支持其他友人/团队卡
        public int personType = 0;
        //int16_t cardId;//支援卡id，不是支援卡就0
        //npc人头的马娘id，不是npc就0，懒得写也可以一律0（只用于获得npc的名字）
        public int charaId = 0;
        //羁绊
        public int friendship = 0;
        //bool atTrain[5];//是否在五个训练里。对于普通的卡只是one-hot或者全空，对于ssr佐岳可能有两个true
        //bool isShining;//是否闪彩。无法闪彩的卡或者npc恒为false
        //是否有hint。友人卡或者npc恒为false
        public bool isHint = false;
        //记录一些可能随着时间而改变的参数，例如根涡轮的固有
        public int cardRecord = 0;

    }

    /// <summary>
    /// 单条训练指令的游戏侧数值。两层都是 7 项定长数组，顺序与剧本
    /// <c>training.normal</c> 的基础值一致：速 / 耐 / 力 / 根 / 智 / 技能点 / 体力（负数为消耗）。
    /// 数值是协议 <c>params_inc_dec_info_array</c> 的原值，<b>未做 1200 衰减</b>（与 <c>fiveStatus</c> 同口径）。
    /// </summary>
    public class TrainingFrame
    {
        /// <summary>该指令的 <c>failure_rate</c>。</summary>
        public int failureRate;
        /// <summary>下层：<c>home_info.command_info_array</c> 的增量，不含剧本加成。</summary>
        public int[] lower = new int[7];
        /// <summary>上层：剧本 <c>xxx_data_set.command_info_array</c> 的加成分量。逐项与 lower 相加即总增量。</summary>
        public int[] upper = new int[7];
    }

    public class GameStatusSend_Base<T>
    where T : PersonBase, new()
    {
        public int scenarioId; // 剧本ID
        public int umaId;//马娘编号，见KnownUmas.cpp
        public int umaStar;//几星
        public bool islegal;//是否为有效的回合数据

        public int turn;//回合数，从0开始，到77结束
        public int vital;//体力，叫做“vital”是因为游戏里就这样叫的
        public int maxVital;//体力上限
        public int motivation;//干劲，从1到5分别是绝不调到绝好调

        public int[] fiveStatus;//五维属性，1200以上不减半
        public int[] fiveStatusLimit;//五维属性上限，1200以上不减半
        public int skillPt;//技能点
        public int skillScore;//已买技能的分数
        public int totalHints; // 总共Hint等级
        public int[] trainLevelCount;
        /// <summary>
        /// 本回合各训练指令的游戏侧数值，key 为指令 id（101–106 / 601–605 / 5101–5105 等，
        /// 剧本专属 id 原样保留，映射交给 AI 端）。只列本回合可训练的指令，不可训练回合为空字典。
        /// 剧本相关：目前仅骏大祭填充，其余剧本保持 null 不输出。
        /// </summary>
        public Dictionary<int, TrainingFrame>? training;

        public double ptScoreRate;
        public int failureRateBias;//失败率改变量。练习上手=2，练习下手=-2

        public bool isQieZhe;//切者
        public bool isXiaoQie;//小切（状态40）
        public bool isAiJiao;//爱娇
        public int PositiveThinkingCount;//心情盾次数：拥有状态25/41/42时各+1，最多3次（每次可防一次掉心情）
        public bool isRefreshMind;//休息的心得,每回合体力+5
        public bool isIll; // 生病，不区分种类（只看 142 表的 1–6）
        /// <summary>
        /// 马娘持有状态：游戏 <c>chara_effect_id_array</c> 原值，按协议顺序去重、过滤非正值。
        /// 含 1–6 病种（142 表：1 熬夜 / 2 懒惰成性 / 3 皮肤粗糙 / 4 变胖 /
        /// 5 偏头痛 / 6 不擅长练习）与全部其它状态（7 切者、25 正向思考、45 消除疲劳、
        /// 47 助力支撑、100–104 情热领域等）。语义由消费端按自己的数据解释。
        /// <c>isIll</c> 只表示 1–6 病种是否存在，与本数组是否非空无关。
        /// </summary>
        public int[] charaEffects = [];
        public int LuckyCount;//幸运体质次数：拥有状态26/43时各+1

        public int[] zhongMaBlueCount;//种马的蓝因子个数，假设只有3星
        public int[] zhongMaExtraBonus;//种马的剧本因子以及技能白因子（等效成pt），每次继承加多少。全大师杯因子典型值大约是30速30力200pt

        public int saihou;
        public bool isRacing;
        public int[] cardId;
        public T[] persons;//依次是6张卡
        public int[,] personDistribution;//每个训练有哪些人头id，personDistribution[哪个训练][第几个人头]，空人头为-1
        public int lockedTrainingId;
        public int friendship_noncard_yayoi;//非卡理事长的羁绊，带了理事长卡就是0
        public int friendship_noncard_reporter;//非卡记者的羁绊

        //单独处理友人卡，因为接近必带。其他友人团队卡的以后再考虑
        //public int friend_type;//0没带友人卡，1 ssr卡，2 r卡
        //public int friend_cardId;  // 单独存放友人卡ID
        //public int friend_personId;//友人卡在persons里的编号
        public int friend_stage;//0未点击，1点击还未解锁出行，2已解锁出行
        public int friend_outgoingUsed;//出行已经走了几段了   暂时不考虑其他友人团队卡的出行

        /// <summary>
        /// 游戏状态，不为1时，为重复回合
        /// </summary>
        public int playing_state;

        /// <summary>
        /// 是否为训练后：当前回合（1 起始）已经执行过任意指令（训练/休息/出行等）。
        /// 来自 EventLogger 记录的最近一次指令回合，-1 表示本局尚未行动。
        /// </summary>
        public bool isAfterTraining;

        /// <summary>
        /// 本局是否从第 1 回合开始完整记录（EventLogger 会话标记）。
        /// false 表示中途加载进入，EventLogger 之前回合的历史数据不完整。
        /// </summary>
        public bool isFullGame;
        public int[] raceHistory;  // 取胜的回合数，从0开始，没赢的不计入

        /// <summary>
        /// 本局已发生的关键事件 story_id 列表（友人首次点击事件、携带支援卡的支援卡连续事件）。
        /// </summary>
        public int[] keyEvents;

        /// <summary>
        /// 每次继承事件获得的属性增量，按继承发生顺序（对应 EventLogger 快照的 InheritStats；
        /// 继承的技能点增量未在 EventLogger 公共快照中暴露，故不含 pt 部分）。
        /// </summary>
        public int[] inheritGains;

        /// <summary>
        /// 每次继承事件带来的五维上限增量，按继承发生顺序，逐项对应 <c>fiveStatusLimit</c> 的五维。
        /// 协议原值，未做 1200 以上不减半换算（与 <c>fiveStatusLimit</c> 不同口径）。
        /// 第 2、3 次继承的上限增量是随机的，不能按蓝因子重算，只能取实测量。
        /// </summary>
        public int[][] inheritLimitGains;

        /// <summary>
        /// 本局累计花掉的技能点（购买技能等导致的 pt 减少，由 EventLogger 逐次累计）。
        /// </summary>
        public int skillPtSpent;

        /// <summary>
        /// 当前育成 ID（single_mode_chara_id）。
        /// 文件名、跨插件状态判断均依赖此字段。
        /// </summary>
        public int single_mode_chara_id;

        /// <summary>
        /// 标识本实例从哪类响应构造。可空，旧的 payload 类保持 null。
        /// 拉面杯场景下："load" / "command" / "event" / "special"。
        /// </summary>
        public string? source { get; set; }

        /// <summary>
        /// 本帧等待玩家选择的事件 <c>story_id</c>（协议原值）。
        /// 只要 <c>unchecked_event_array</c> 里存在多选项（<c>choice_array.Length > 1</c>）就设置，
        /// 与事件效果表是否收录无关；消费端据此自行查表判断。空值时不输出该字段。
        /// 同时作为 <see cref="ResolveSource"/> 判定「事件帧」的信号。
        /// </summary>
        public int? storyId;

        /// <summary>
        /// 事件效果表收录时附带的效果数据（含选项效果）；未收录或表未加载时为 null，不输出。
        /// 是否发送由 <see cref="storyId"/> 决定，与本字段无关。
        /// </summary>
        public Story story;

        /// <summary>
        /// 基于本实例的 <see cref="source"/> / <see cref="playing_state"/> / <see cref="storyId"/> 字段
        /// 与外部传入的 <paramref name="uncheckedEventArray"/> 综合解析为最终 source。
        /// 返回 null 表示本帧不发送（调用方应跳过 doSend）。
        /// 规则（按本实例的 source 分支）：
        /// - "command" + storyId 有值                  → "event"（响应携带待选事件，优先于 command）
        /// - "command" + UncheckedEventArray 有内容    → null（仅自动事件进行中，不保存）
        /// - "command" + playing_state == 1          → "command"
        /// - "command" + 1 < playing_state < 10      → null（中间状态，跳过发送）
        /// - "command" + playing_state >= 10         → "special"
        /// - "event" + storyId 有值                   → "event"（有手动选项即保存，效果记录非必要）
        /// - "event" + storyId 空 + UncheckedEventArray 有内容 → null（仅自动事件进行中，不保存）
        /// - "event" + storyId 空 + UncheckedEventArray 为空 + 1 < playing_state < 10 → null
        /// - "event" + storyId 空 + UncheckedEventArray 为空 + playing_state >= 10 → "special"（结算）
        /// - "event" + storyId 空 + UncheckedEventArray 为空 + playing_state 其它 → "command"
        /// - 其它（load / 业务自定义）                → 原样保留
        /// </summary>
        public string? ResolveSource(SingleModeEventInfo[]? uncheckedEventArray)
            => source switch
            {
                "command" when storyId is not null                    => "event",
                "command" when uncheckedEventArray is { Length: > 0 } => null,
                "command" when playing_state == 1                     => "command",
                "command" when playing_state < 10                     => null,
                "command"                                             => "special",
                "event" when storyId is not null                      => "event",
                "event" when uncheckedEventArray is { Length: > 0 }   => null,
                "event" when uncheckedEventArray is null or { Length: 0 }
                                           && playing_state > 1
                                           && playing_state < 10       => null,
                // 剧本结算（ps 49/51 等 >=10）不是回合开始，与 command 分支同口径标 special。
                "event" when playing_state >= 10                       => "special",
                "event"                                               => "command",
                _                                                     => source,
            };

        public bool isRepeatTurn()
        {
            return this.playing_state != 1;
        }

        public GameStatusSend_Base(Gallop.SingleModeMechaCheckEventResponse @event)
            : this(CheckEventContext.From(@event), EventLogger.Current)
        {
        }

        public GameStatusSend_Base(Gallop.SingleModeOnsenCheckEventResponse @event)
            : this(CheckEventContext.From(@event), EventLogger.Current)
        {
        }

        internal GameStatusSend_Base(
            Gallop.SingleModeOnsenCheckEventResponse @event,
            EventLoggerRoundSnapshot round)
            : this(CheckEventContext.From(@event), round)
        {
        }

        internal GameStatusSend_Base(
            Gallop.SingleModeRamenCheckEventResponse @event,
            EventLoggerRoundSnapshot round)
            : this(CheckEventContext.From(@event), round)
        {
        }

        internal GameStatusSend_Base(
            Gallop.SingleModeFestaCheckEventResponse @event,
            EventLoggerRoundSnapshot round)
            : this(CheckEventContext.From(@event), round)
        {
        }

        private GameStatusSend_Base(CheckEventContext @event, EventLoggerRoundSnapshot round)
        {
            islegal = false;
            single_mode_chara_id = @event.data.chara_info.single_mode_chara_id;
            playing_state = @event.data.chara_info.playing_state;

            //if(@event.data.race_start_info != null)
            isRacing = true;
            for (var i = 0; i < 5; i++)
            {
                isRacing &= (@event.data.home_info.command_info_array[i].is_enable == 0);
            }

            islegal = true;
            umaId = @event.data.chara_info.card_id;
            umaStar = @event.data.chara_info.rarity;
            //turn
            var turnNum = @event.data.chara_info.turn;//游戏里回合数从1开始
            turn = turnNum - 1;//ai里回合数从0开始
            //是否为训练后：EventLogger 记录的最近一次指令回合（1 起始，-1 表示本局尚未行动）不早于当前回合
            isAfterTraining = round.AfterTrainingTurn >= turnNum;
            //本局是否从第1回合开始完整记录
            isFullGame = round.IsFullGame;
            vital = @event.data.chara_info.vital;
            maxVital = @event.data.chara_info.max_vital;
            motivation = @event.data.chara_info.motivation;
            // raceHistory
            var history = new List<int>();
            foreach (var t in round.RaceHistory)
            {
                history.Add(t - 1);
            }
            raceHistory = history.ToArray();

            // keyEvents 与 inheritGains：story_id 与属性增量均保持原值，不做回合数换算
            keyEvents = round.KeyEvents.ToArray();
            inheritGains = round.InheritStats.ToArray();
            inheritLimitGains = round.InheritLimitGains.Select(x => x.ToArray()).ToArray();

            // 本局累计花掉的技能点
            skillPtSpent = round.SkillPtSpent;

            fiveStatus = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.wiz) ,
            };

            fiveStatusLimit = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_wiz) ,
            };

            failureRateBias = 0;
            // 全量状态原值透传：只做「过滤非正值 / 去重」，不做语义分类，顺序沿用协议。
            charaEffects = (@event.data.chara_info.chara_effect_id_array ?? [])
                .Where(effect => effect > 0)
                .Distinct()
                .ToArray();
            // isIll 仍是「有没有病」，不能按数组非空判断（否则只有 buff 的回合会误判成生病）。
            isIll = charaEffects.Any(effect => effect is >= 1 and <= 6);

            foreach (var effect in charaEffects)
            {
                switch (effect)
                {
                    case 6:
                        failureRateBias = 2; break;
                    case 10:
                        failureRateBias = -2; break;
                    case 7:
                        isQieZhe = true; break;
                    case 40:
                        isXiaoQie = true; break;
                    case 8:
                        isAiJiao = true; break;
                    case 25:
                    case 41:
                    case 42:
                        PositiveThinkingCount += 1; break;
                    case 26:
                    case 43:
                        LuckyCount += 1; break;
                    case 32:
                        isRefreshMind = true; break;
                }
            }
            ptScoreRate = 2.0;

            skillPt = @event.data.chara_info.skill_point;

            // 总 Hint 等级：skill_tips_array 每项 level 之和。字段缺失时记 0。
            totalHints = (@event.data.chara_info.skill_tips_array ?? []).Sum(tip => tip.level);

            skillScore = 0;
            cardId = new int[6];

            zhongMaBlueCount = new int[5];
            //用属性上限猜蓝因子个数
            {
                var defaultLimit = GameGlobal.FiveStatusLimit[@event.data.chara_info.scenario_id];
                double factor = 16;//每个三星因子可以提多少上限
                if (turn >= 54)//第二次继承结束
                    factor = 22;
                else if (turn >= 30)//第二次继承结束
                    factor = 19;
                for (var i = 0; i < 5; i++)
                {
                    var div = (defaultLimit[i] >= 1200 ? 2 : 1);
                    var threeStarCount = (int)Math.Round((fiveStatusLimit[i] - defaultLimit[i]) / div / factor);
                    if (threeStarCount > 6) threeStarCount = 6;
                    if (threeStarCount < 0) threeStarCount = 0;
                    zhongMaBlueCount[i] = threeStarCount * 3;
                }
            }

            trainLevelCount = new int[5] { 0, 0, 0, 0, 0 };

            var trainLevelClickNumEvery = 4;
            var turnStat = round.Turns[@event.data.chara_info.turn];
            if (turnStat == null)
            {
                for (var i = 0; i < 5; i++)
                {
                    var trId = @event.data.chara_info.scenario_id == (int)ScenarioType.Mecha ? GameGlobal.TrainIdsMecha[i] :
                        GameGlobal.TrainIds[i];
                    var trLevel = @event.data.chara_info.training_level_info_array.First(x => x.command_id == trId).level;
                    var count = (trLevel - 1) * trainLevelClickNumEvery;
                    trainLevelCount[i] = count;
                }
            }
            else
            {
                for (var i = 0; i < 5; i++)
                    trainLevelCount[i] = turnStat.TrainLevelCount[i] + trainLevelClickNumEvery * (turnStat.TrainLevel[i] - 1);
            }

            //从游戏json的id到ai的人头编号的换算
            foreach (var s in @event.data.chara_info.support_card_array)
            {
                var p = s.position - 1;
                //突破数+10*卡原来的id，例如神团是30137，满破神团就是301374
                cardId[p] = s.limit_break_count + s.support_card_id * 10;
            }

            persons = new T[6];
            for (var i = 0; i < 6; i++)
                persons[i] = new T();

            var friend_type = 0;
            var friend_personId = -1;
            for (var i = 0; i < 6; i++)
            {
                var personJson = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == i + 1);
                persons[i].cardRecord = 0;
                persons[i].friendship = personJson.evaluation;
                switch (cardId[i] / 10)
                {
                    case 30207://ssr 理事长
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 1;
                        break;
                    case 10109://r 理事长
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 1;
                        break;
                    case 30188://ssr 凉花
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 2;
                        break;
                    case 10104://r 凉花
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 2;
                        break;
                    case 10138: // r健子
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 1;
                        break;
                    case 30276: // ssr健子
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 2;
                        break;
                    case 30305: // ssr绿帽
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 2;
                        break;
                    case 30323: // ssr 吾妻（骏大祭友人）
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 1;
                        break;
                    case 10149: // r 吾妻（骏大祭友人）
                        persons[i].personType = 1;
                        friend_personId = i;
                        friend_type = 1;
                        break;
                    default:
                        persons[i].personType = 2;
                        break;
                }
            }
            friendship_noncard_yayoi = @event.data.chara_info.evaluation_info_array.Any(x => x.target_id == 102) ?
                @event.data.chara_info.evaluation_info_array.First(x => x.target_id == 102).evaluation : 0;
            friendship_noncard_reporter = @event.data.chara_info.evaluation_info_array.Any(x => x.target_id == 103) ?
                @event.data.chara_info.evaluation_info_array.First(x => x.target_id == 103).evaluation : 0;

            personDistribution = new int[5, 5];
            for (var i = 0; i < 5; i++)
                for (var j = 0; j < 5; j++)
                    personDistribution[i, j] = -1;

            foreach (var train in @event.data.home_info.command_info_array)
            {
                if (!GameGlobal.ToTrainIndex.ContainsKey(train.command_id))//不是正常训练
                    continue;
                var trainId = GameGlobal.ToTrainIndex[train.command_id];

                var j = 0;
                foreach (var p in train.training_partner_array)
                {
                    var personIdUmaAi = p == 102 ? 6 : p == 103 ? 7 : p >= 1000 ? 8 : p - 1;
                    personDistribution[trainId, j] = personIdUmaAi;
                    j += 1;
                }
                foreach (var p in train.tips_event_partner_array)
                {

                    persons[p - 1].isHint = true;
                }
            }

            //计算Lockedtrainid
            {
                var istrainlocked = false;
                var enableidx = -1;
                foreach (var train in @event.data.home_info.command_info_array)
                {
                    if (!GameGlobal.ToTrainIndex.ContainsKey(train.command_id))//不是正常训练
                        continue;
                    if (train.is_enable != 1)
                    {
                        istrainlocked = true;
                    }
                    else
                    {
                        enableidx = Convert.ToInt32(train.command_id) % 10;
                    }
                }

                if (istrainlocked)
                {
                    lockedTrainingId = enableidx;
                }
                else
                {
                    lockedTrainingId = -1;
                }
            }

            friend_stage = 0;
            //友人出行用了几次
            if (friend_type != 0)
            {
                var friendJson = @event.data.chara_info.evaluation_info_array.First(x => x.target_id == friend_personId + 1);
                friend_outgoingUsed = friendJson.story_step;
                if (friendJson.is_outing == 1)
                    friend_stage = 2;
                else
                {
                    var friendClicked = false;//友人卡是否点过第一次
                    for (var t = @event.data.chara_info.turn - 1; t >= 1; t--)
                    {
                        if (round.Turns[t] is not { } stats)
                        {
                            break;
                        }

                        if (!GameGlobal.TrainIds.Any(x => x == stats.PlayerChoice)) //没训练
                            continue;
                        if (stats.IsTrainingFailed)//训练失败
                            continue;
                        if (!stats.CookFriendAtTrain[GameGlobal.ToTrainIndex[stats.PlayerChoice]])
                            continue;//没点友人

                        friendClicked = true;
                        break;
                    }
                    if (friendClicked) friend_stage = 1;
                    else friend_stage = 0;
                }

            }
            else
            {
                friend_outgoingUsed = 0;
            }

            // 多选项（>1）即视为需要玩家手动选择：记录 story_id 并据此发帧。
            // 效果表只是附带数据，查不到也照发，由消费端按 storyId 自行处理。
            // 一串事件里优先取第一个有手动选项的；连续到达的事件帧各自落盘，不做去重。
            foreach (var evt in @event.data.unchecked_event_array ?? [])
            {
                if (evt is null || evt.event_contents_info?.choice_array is not { Length: > 1 })
                    continue;

                storyId = evt.story_id;
                if (Database.Availability == DatabaseAvailability.Ready
                    && Database.Events.TryGetValue(evt.story_id, out var known))
                    story = known;
                break;
            }
        }

        public void doSend()
        {
            if (this.islegal == false)
            {
                return;
            }
            GameStatusOutput.WriteScenarioData(this, turn);
        }
    }
}
