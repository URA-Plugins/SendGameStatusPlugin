using System.Runtime.CompilerServices;
using Gallop;

namespace SendGameStatusPlugin;

/// <summary>
/// 骏大祭（scenario 15）三类 response（Load / Start / ExecCommand / CheckEvent）的统一中间类型。
/// 各 RegisterAnalyzer 入口先用 FromXxx 工厂映射为此类型，再交给 AnalyzeFesta 统一处理。
/// </summary>
public sealed record FestaResponse(
    SingleModeChara? CharaInfo,
    SingleModeHomeInfo? HomeInfo,
    SingleModeEventInfo[]? UncheckedEventArray,
    SingleRaceStartInfo? RaceStartInfo,
    SingleModeFestaDataSet? FestaDataSet,
    string? Source = null)
{
    public static FestaResponse FromLoad(SingleModeFestaLoadResponse r)
    {
        var loadCommon = r.data?.single_mode_load_common;
        return new FestaResponse(
            CharaInfo: loadCommon?.chara_info,
            HomeInfo: loadCommon?.home_info,
            UncheckedEventArray: loadCommon?.unchecked_event_array,
            RaceStartInfo: loadCommon?.race_start_info,
            FestaDataSet: r.data?.festa_data_set,
            Source: "load");
    }

    public static FestaResponse FromStart(SingleModeFestaStartResponse r)
    {
        var startCommon = r.data?.single_mode_start_common;
        return new FestaResponse(
            CharaInfo: startCommon?.chara_info,
            HomeInfo: startCommon?.home_info,
            UncheckedEventArray: startCommon?.unchecked_event_array,
            RaceStartInfo: null,
            FestaDataSet: r.data?.festa_data_set,
            Source: "load");
    }

    public static FestaResponse FromExecCommand(SingleModeFestaExecCommandResponse r)
        => new(
            CharaInfo: r.data.chara_info,
            HomeInfo: r.data.home_info,
            UncheckedEventArray: r.data.unchecked_event_array,
            RaceStartInfo: null,
            FestaDataSet: r.data.festa_data_set,
            Source: "command");

    public static FestaResponse FromCheckEvent(SingleModeFestaCheckEventResponse r)
        => new(
            CharaInfo: r.data.chara_info,
            HomeInfo: r.data.home_info,
            UncheckedEventArray: r.data.unchecked_event_array,
            RaceStartInfo: r.data.race_start_info,
            FestaDataSet: r.data.festa_data_set,
            Source: "event");
}

public partial class SendGameStatusPlugin
{
    ValueTask AnalyzeFesta(FestaResponse data)
    {
        if (data.CharaInfo is null || data.HomeInfo?.command_info_array is not { Length: >= 5 }
            || !pluginContext!.IsPluginAvailable("EventLoggerPlugin")
            || !pluginContext.IsPluginAvailable("FestaScenarioAnalyzer"))
            return ValueTask.CompletedTask;

        return AnalyzeFestaWithDependencies(data);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ValueTask AnalyzeFestaWithDependencies(FestaResponse data)
    {
        // 反向映射到 CheckEventResponse 形态，复用 GameStatusSend_Festa(CheckEventResponse) 构造。
        var checkEventResponse = new SingleModeFestaCheckEventResponse
        {
            data = new()
            {
                chara_info = data.CharaInfo,
                gain_parameter_info = null,
                not_up_parameter_info = null,
                not_down_parameter_info = null,
                gain_partner_support_effect_array = null,
                home_info = data.HomeInfo,
                unchecked_event_array = data.UncheckedEventArray,
                event_effected_factor_array = null,
                race_condition_array = null,
                race_start_info = data.RaceStartInfo,
                race_running_style = 0,
                select_index = 0,
                select_index_info_array = null,
                festa_data_set = data.FestaDataSet
            }
        };

        var send = new GameStatusSend_Festa(checkEventResponse);
        if (!send.baseGame.islegal || send.festa is null)
            return ValueTask.CompletedTask;

        // base 实例方法：基于自身 source / playing_state / story 与外部 UncheckedEventArray
        // 解析最终 source；返回 null 表示本帧不发送（中间态、待选事件帧）。
        send.baseGame.source = data.Source;
        var finalSource = send.baseGame.ResolveSource(data.UncheckedEventArray);
        if (finalSource is null)
            return ValueTask.CompletedTask;

        send.baseGame.source = finalSource;

        // 事件 / 结算帧的等级不可信（协议把基础项填成默认 1）：按「无有效值」发 0；
        // load / command 帧用当帧真值更新基准，缺失的位用基准补（合宿、终盘稽古段）。
        send.ApplyLevelBaseline(finalSource);

        send.doSend();
        return ValueTask.CompletedTask;
    }
}
