using EventLoggerPlugin;
using Gallop;
using RamenScenarioAnalyzer;

namespace SendGameStatusPlugin
{

    public class RamenActiveEffect
    {
        public int category;
        public int id;
        public int value;
    }

    public class RamenStatus
    {
        // 训练按 command_id 顺序：101=速, 105=耐, 102=力, 103=根, 106=智
        static readonly int[] TrainCommandOrder = { 101, 105, 102, 103, 106 };

        public int[,] feeling_gauge_gains = new int[5, 3];  // 诀窍槽增量
        public int[] feeling_gauge = new int[3];            // 诀窍槽当前值，反推自 feeling_turn_info_array.remain_turn
        public List<int> feeling_stock = new List<int>();   // 诀窍队列
        public int special_feeling = 0; // 隐藏风味
        public int[] train_feeling_type = new int[5]; // 训练角标
        public List<RamenActiveEffect> active_effect_array = new List<RamenActiveEffect>();
        public int super_ramen = -1;    // 超级拉面种类
        // 还有一些数据必须从Load得到
        public int[] selected_regions = new int[3];    // 选择地区
        public int[] feeling_gauge_gain_base = new int[3];  // reduce_base_turn
        public int last_ramen = -1;    // 上一次拉面种类 -1为没吃 0还要观察下
        public int scenario_pt = 0; // checkpoint_pt
        public int next_scenario_pt = 0;

        public RamenStatus(SingleModeRamenDataSet dataset, RamenStateSnapshot? state)
        {
            // 1. feeling_gauge_gains：按 feeling_reduce_turn_info_array 顺序填充
            //    训练按 command_id ∈ {101,105,102,103,106} 顺序，组内按 feeling_turn_array 顺序
            //    索引方向：[训练, feeling_id - 1]
            //    command_id > 600 时先 -500 归一化到 base id
            if (dataset.feeling_reduce_turn_info_array != null)
            {
                foreach (var reduce in dataset.feeling_reduce_turn_info_array)
                {
                    var baseCmdId = reduce.command_id > 600 ? reduce.command_id - 500 : reduce.command_id;
                    var trainIdx = Array.IndexOf(TrainCommandOrder, baseCmdId);
                    if (trainIdx < 0 || reduce.feeling_turn_array == null) continue;
                    foreach (var ft in reduce.feeling_turn_array)
                    {
                        if (ft.feeling_id >= 1 && ft.feeling_id <= 3)
                        {
                            feeling_gauge_gains[trainIdx, ft.feeling_id - 1] = ft.turn;
                        }
                    }
                }
            }

            // 2. feeling_gauge：反推自 feeling_turn_info_array.remain_turn
            //    feeling_gauge[i] = 7 - remain_turn
            if (dataset.feeling_turn_info_array != null)
            {
                foreach (var info in dataset.feeling_turn_info_array)
                {
                    if (info.feeling_id >= 1 && info.feeling_id <= 3)
                    {
                        feeling_gauge[info.feeling_id - 1] = 7 - info.remain_turn;
                    }
                }
            }

            // 3. feeling_stock：feeling_info_array.feeling_id，忽略 feeling_id == 0
            //    顺序与原数组一致（feeling_id > 0 在前）
            if (dataset.feeling_info_array != null)
            {
                foreach (var info in dataset.feeling_info_array)
                {
                    if (info.feeling_id > 0)
                    {
                        feeling_stock.Add(info.feeling_id);
                    }
                }
            }

            // 4. special_feeling ← special_feeling_num
            special_feeling = dataset.special_feeling_num;

            // 5. train_feeling_type：严格按 [101,105,102,103,106] 顺序填充
            //    command_feeling_info_array 中 command_id > 600 时先 -500 归一化到 base id
            if (dataset.command_feeling_info_array != null)
            {
                for (var i = 0; i < TrainCommandOrder.Length; i++)
                {
                    var baseId = TrainCommandOrder[i];
                    var match = dataset.command_feeling_info_array
                        .FirstOrDefault(x => x.command_id == baseId
                                          || (x.command_id > 600 && x.command_id - 500 == baseId));
                    train_feeling_type[i] = match?.feeling_id ?? 0;
                }
            }

            // 6. active_effect_array：字段名带 effect_ 前缀，逐字段赋值
            if (dataset.active_effect_array != null)
            {
                foreach (var effect in dataset.active_effect_array)
                {
                    active_effect_array.Add(new RamenActiveEffect
                    {
                        category = effect.effect_category,
                        id = effect.effect_id,
                        value = effect.effect_value
                    });
                }
            }

            // 7. super_ramen ← uraf_effect_info.uraf_effect_type
            // super_ramen, selected_regions, last_ramen 需要调整为从0开始
            if (dataset.uraf_effect_info != null)
            {
                super_ramen = dataset.uraf_effect_info.uraf_effect_type - 1;
            }

            // 8. Load 独有字段：从 RamenScenarioState 快照填充；取不到（null）时保持默认值。
            
            if (state is not null)
            {
                selected_regions = (int[])state.selected_region_id_array
                    .Select(x => x - 1)
                    .ToArray();
                feeling_gauge_gain_base = (int[])state.reduce_base_turn.Clone();
                last_ramen = state.last_ramen - 1;
                scenario_pt = state.check_point_pt;
                next_scenario_pt = state.expected_check_point_pt;
            }
        }
    }

    public class GameStatusSend_Ramen : IHasBaseGame
    {
        public GameStatusSend_Base<PersonBase> baseGame { get; set; }
        public RamenStatus? ramen;

        private static Gallop.SingleModeRamenCheckEventResponse ToCheckEventResponse(Gallop.SingleModeRamenExecCommandResponse @event) => new()
        {
            data = new()
            {
                chara_info = @event.data.chara_info,
                gain_parameter_info = @event.data.gain_parameter_info,
                not_up_parameter_info = @event.data.not_up_parameter_info,
                not_down_parameter_info = @event.data.not_down_parameter_info,
                gain_partner_support_effect_array = @event.data.gain_partner_support_effect_array,
                home_info = @event.data.home_info,
                unchecked_event_array = @event.data.unchecked_event_array,
                race_condition_array = @event.data.race_condition_array,
                race_start_info = null,
                ramen_data_set = @event.data.ramen_data_set
            }
        };

        /// <summary>
        /// 把 Load 响应映射为 CheckEvent 响应形态，便于复用同一构造路径。
        /// Load 没有的字段填默认值；race_start_info 强制 null。
        /// </summary>
        private static Gallop.SingleModeRamenCheckEventResponse ToCheckEventResponse(Gallop.SingleModeRamenLoadResponse @event)
        {
            var loadCommon = @event.data?.single_mode_load_common;
            return new()
            {
                data = new()
                {
                    chara_info = loadCommon?.chara_info,
                    gain_parameter_info = null,
                    not_up_parameter_info = null,
                    not_down_parameter_info = null,
                    gain_partner_support_effect_array = null,
                    home_info = loadCommon?.home_info,
                    unchecked_event_array = loadCommon?.unchecked_event_array,
                    event_effected_factor_array = null,
                    race_condition_array = null,
                    race_start_info = null,
                    race_running_style = 0,
                    select_index = 0,
                    select_index_info_array = null,
                    ramen_data_set = @event.data?.ramen_data_set,
                    ramen_data_set_check_event = null
                }
            };
        }

        public GameStatusSend_Ramen(Gallop.SingleModeRamenCheckEventResponse @event)
        {
            var round = EventLogger.Current;
            baseGame = new GameStatusSend_Base<PersonBase>(@event, round);
            baseGame.scenarioId = 14;
            baseGame.source = "event";
            if (@event.data.ramen_data_set != null)
            {
                // 不再校验 loaded / charaId 是否匹配，始终把当前快照交给 RamenStatus。
                // RamenStatus 内部按字段尽量取快照值；快照无效时由字段默认值兜底。
                var state = RamenScenarioState.Snapshot();
                ramen = new RamenStatus(@event.data.ramen_data_set, state);
            }
        }

        public GameStatusSend_Ramen(SingleModeRamenLoadResponse @event)
            : this(ToCheckEventResponse(@event))
        {
            baseGame.source = "load";
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
