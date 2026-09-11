using System.Runtime.CompilerServices;
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

        public RamenStatus(SingleModeRamenDataSet dataset)
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

        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void ReadState()
        {
            var state = RamenScenarioState.Snapshot();
            selected_regions = state.selected_region_id_array.Select(x => x - 1).ToArray();
            feeling_gauge_gain_base = (int[])state.reduce_base_turn.Clone();
            last_ramen = state.last_ramen == -1 ? -1 : state.last_ramen - 1;
            scenario_pt = state.check_point_pt;
            next_scenario_pt = state.expected_check_point_pt;
        }
    }

    public class GameStatusSend_Ramen : IHasBaseGame
    {
        public GameStatusSend_Base<PersonBase> baseGame { get; set; }
        public RamenStatus? ramen;

        internal GameStatusSend_Ramen(SingleModeRamenCheckEventResponse @event, bool hasRamenState)
        {
            var round = EventLogger.Current;
            baseGame = new GameStatusSend_Base<PersonBase>(@event, round);
            baseGame.scenarioId = 14;
            if (@event.data.ramen_data_set != null)
            {
                ramen = new RamenStatus(@event.data.ramen_data_set);
                if (hasRamenState)
                    ramen.ReadState();
            }
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
