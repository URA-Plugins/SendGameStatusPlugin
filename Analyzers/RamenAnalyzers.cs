using System.Runtime.CompilerServices;
using Gallop;

namespace SendGameStatusPlugin;

/// <summary>
/// 拉面杯路径上三种 response（Load / CheckEvent / ExecCommand）的统一中间类型。
/// 各 RegisterAnalyzer 入口先用 FromXxx 工厂把 response 映射为此类型，再交给 AnalyzeRamen 统一处理。
/// </summary>
public sealed record RamenResponse(
    SingleModeChara? CharaInfo,
    SingleModeHomeInfo? HomeInfo,
    SingleModeEventInfo[]? UncheckedEventArray,
    SingleRaceStartInfo? RaceStartInfo,
    SingleModeRamenDataSet? RamenDataSet,
    SingleModeRamenDataSetLoad? RamenDataSetLoad,
    string? Source = null)
{
    /// <summary>
    /// 把 SingleModeRamenLoadResponse 映射为 RamenResponse。
    /// </summary>
    public static RamenResponse FromLoad(SingleModeRamenLoadResponse r)
    {
        var loadCommon = r.data?.single_mode_load_common;
        return new RamenResponse(
            CharaInfo: loadCommon?.chara_info!,
            HomeInfo: loadCommon?.home_info,
            UncheckedEventArray: loadCommon?.unchecked_event_array,
            RaceStartInfo: null,
            RamenDataSet: r.data?.ramen_data_set,
            RamenDataSetLoad: r.data?.ramen_data_set_load,
            Source: "load");
    }

    /// <summary>
    /// 把 SingleModeRamenCheckEventResponse 映射为 RamenResponse。
    /// 钩子要求 baseGame.story 非空时才发。
    /// </summary>
    public static RamenResponse FromCheckEvent(SingleModeRamenCheckEventResponse r)
        => new(
            CharaInfo: r.data.chara_info,
            HomeInfo: r.data.home_info,
            UncheckedEventArray: r.data.unchecked_event_array,
            RaceStartInfo: r.data.race_start_info,
            RamenDataSet: r.data.ramen_data_set,
            RamenDataSetLoad: null,
            Source: "event");

    /// <summary>
    /// 把 SingleModeRamenExecCommandResponse 映射为 RamenResponse。
    /// </summary>
    public static RamenResponse FromExecCommand(SingleModeRamenExecCommandResponse r)
        => new(
            CharaInfo: r.data.chara_info,
            HomeInfo: r.data.home_info,
            UncheckedEventArray: r.data.unchecked_event_array,
            RaceStartInfo: null,
            RamenDataSet: r.data.ramen_data_set,
            RamenDataSetLoad: null,
            Source: "command");
}

public partial class SendGameStatusPlugin
{
    ValueTask AnalyzeRamen(RamenResponse data)
    {
        if (data.CharaInfo is null || data.HomeInfo?.command_info_array is not { Length: >= 5 }
            || !pluginContext!.IsPluginAvailable("EventLoggerPlugin"))
            return ValueTask.CompletedTask;

        return AnalyzeRamenWithDependencies(data, pluginContext.IsPluginAvailable("RamenScenarioAnalyzer"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ValueTask AnalyzeRamenWithDependencies(RamenResponse data, bool hasRamenState)
    {
        // 反向映射到 CheckEventResponse 形态，复用 GameStatusSend_Ramen(CheckEventResponse) 构造。
        var checkEventResponse = new SingleModeRamenCheckEventResponse
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
                ramen_data_set = data.RamenDataSet,
                ramen_data_set_check_event = null
            }
        };

        var send = new GameStatusSend_Ramen(checkEventResponse, hasRamenState);
        if (!send.baseGame.islegal)
            return ValueTask.CompletedTask;

        // base 实例方法：基于自身 source / playing_state / story 与外部 UncheckedEventArray
        // 解析最终 source；返回 null 表示本帧不发送。
        send.baseGame.source = data.Source;
        var finalSource = send.baseGame.ResolveSource(data.UncheckedEventArray);
        if (finalSource is null)
            return ValueTask.CompletedTask;

        send.baseGame.source = finalSource;
        send.doSend();
        return ValueTask.CompletedTask;
    }
}
