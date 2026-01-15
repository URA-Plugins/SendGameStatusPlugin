using EventLoggerPlugin;
using Gallop;
using Newtonsoft.Json;
using Spectre.Console;

namespace SendGameStatusPlugin
{
    public class OnsenBathingStatus
    {
        public int ticketNum;   // 温泉券数量
        public int buffRemainTurn; // buff剩余回合数
        public bool isSuperReady; // 下一个是否超回复
    }

    public class OnsenStatus
    {
        public int currentOnsen = -1;    // 当前温泉ID
        public OnsenBathingStatus bathing = new();  // 温泉Buff状态
        public bool[] onsenState = new bool[10];    // 温泉挖掘状态
        public int[,] digRemain = new int[10, 3];   // 当前每个温泉的剩余挖掘量
        public int digCount;    // 挖了几个温泉
        public int[] digPower = new int[3]; // 当前挖掘力加成
        public int[] digLevel = { 1, 1, 1 }; // 当前挖掘等级
        public int digVitalCost; // 挖掘累计消耗的体力
        public bool pendingSelection; // 是否需要选择温泉

        private static int GetStratumType(int stratumId)
        {
            // 砂质: stratum_id = 4, 7, 9 , 15, 18
            // 土质: stratum_id = 5, 8, 11, 13, 16, 19 
            // 岩石: stratum_id = 6, 10, 12, 14, 17, 20
            return stratumId switch
            {
                4 or 7 or 9 or 15 or 18 => 0,  // 砂质
                5 or 8 or 11 or 13 or 16 or 19 => 1,  // 土质
                6 or 10 or 12 or 17 or 14 or 20 => 2,  // 岩石
                _ => -1
            };
        }

        public OnsenStatus(Gallop.SingleModeCheckEventResponse @event)
        {
            digVitalCost = EventLogger.vitalSpent;
            // 是否为选择温泉状态
            if (@event.data.chara_info.playing_state == 36)
                pendingSelection = true;

            var dataset = @event.data.onsen_data_set;
            if (dataset != null)
            {
                var bathingInfo = dataset.bathing_info;
                if (bathingInfo != null)
                {
                    bathing.buffRemainTurn = bathingInfo.onsen_effect_remain_count;
                    bathing.ticketNum = bathingInfo.ticket_num;
                    bathing.isSuperReady = bathingInfo.superior_state > 0;
                    // 仅凭当前回合无法获得“当前生效的buff是否为超回复”的信息，而且已经生效的Buff对AI也没有意义
                }
                foreach (var x in dataset.dug_onsen_id_array)
                {
                    onsenState[x-1] = true;
                }
                digCount = dataset.dug_onsen_id_array.Length - 1;   // 不算初始温泉
                for (var i=0; i<dataset.onsen_info_array.Length; i++)
                {
                    if (dataset.onsen_info_array[i].state == 2)
                        currentOnsen = i;
                    for (var j=0; j<dataset.onsen_info_array[i].stratum_info_array.Length; j++)
                    {
                        var stratumInfo = dataset.onsen_info_array[i].stratum_info_array[j];                        
                        if (stratumInfo.rest_volume > 0)
                        {
                            var digType = GetStratumType(stratumInfo.stratum_id);
                            if (digType >= 0)
                            {
                                digRemain[i, digType] = stratumInfo.rest_volume;
                            }
                        }
                    }
                }
                for (var i=0; i<dataset.dig_effect_info_array.Length; i++)
                {
                    var value = dataset.dig_effect_info_array[i];
                    digPower[i] = value.dig_effect_value;
                    digLevel[i] = value.item_level;
                }
            }
        }
    }

    public class GameStatusSend_Onsen
    {
        public GameStatusSend_Base<PersonBase> baseGame;
        public OnsenStatus onsen;

        public GameStatusSend_Onsen(Gallop.SingleModeCheckEventResponse @event)
        {
            baseGame = new GameStatusSend_Base<PersonBase>(@event);
            baseGame.scenarioId = 12;
            onsen = new OnsenStatus(@event);
        }

        public void doSend()
        {
            if (this.baseGame.islegal == false)
            {
                return;
            }
            //var wsSubscribeCount = SubscribeAiInfo.Signal(this);
            //if (wsSubscribeCount > 0 && !this.isRepeatTurn())

            var currentGSdirectory = Path.Combine("PluginData", "SendGameStatusPlugin");
            Directory.CreateDirectory(currentGSdirectory);
            var success = false;
            var tried = 0;
            do
            {
                try
                {
                    var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }; // 去掉空值避免C++端抽风
                    File.WriteAllText($@"{currentGSdirectory}/thisTurn.json", JsonConvert.SerializeObject(this, Formatting.Indented, settings));
                    File.WriteAllText($@"{currentGSdirectory}/turn{this.baseGame.turn}.json", JsonConvert.SerializeObject(this, Formatting.Indented, settings));
                    success = true; // 写入成功，跳出循环
                    break;
                }
                catch
                {
                    tried++;
                    AnsiConsole.MarkupLine("[yellow]写入失败[/]");
                }
            } while (!success && tried < 10);
            if (success)
            {
                AnsiConsole.MarkupLine("[teal]回合已保存，等待AI计算[/]");
            } else 
            {
                AnsiConsole.MarkupLine($@"[red]写入{currentGSdirectory}/thisTurn.json失败！[/]");
            }
        }
    }
}