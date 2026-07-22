using Gallop;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    static ValueTask AnalyzeLegend(SingleModeLegendCheckEventResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array,
                data.race_start_info)
            : ValueTask.CompletedTask;

    static ValueTask AnalyzeLegendLoad(SingleModeLegendLoadResponse response)
    {
        if (response.data is not { } data || data.single_mode_load_common is not { } loadCommon)
            return ValueTask.CompletedTask;

        return AnalyzeLegendResponse(
            loadCommon.chara_info,
            loadCommon.home_info,
            data.legend_data_set,
            loadCommon.unchecked_event_array,
            loadCommon.race_start_info);
    }
}
