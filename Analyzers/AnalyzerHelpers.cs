using Gallop;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    static bool ShouldSkip(
        SingleModeHomeInfo? homeInfo,
        SingleModeEventInfo[]? uncheckedEvents,
        SingleRaceStartInfo? raceStartInfo,
        int state)
    {
        if (homeInfo?.command_info_array is null || state is 2 or 3)
            return true;

        return (uncheckedEvents != null && uncheckedEvents.Length > 0) || raceStartInfo != null;
    }

    static ValueTask AnalyzeLegendResponse(
        SingleModeChara? charaInfo,
        SingleModeHomeInfo? homeInfo,
        SingleModeLegendDataSet? dataSet,
        SingleModeEventInfo[]? uncheckedEventArray = null,
        SingleRaceStartInfo? raceStartInfo = null)
    {
        if (charaInfo is null || homeInfo?.command_info_array is null || dataSet is null)
            return ValueTask.CompletedTask;

        var response = new SingleModeLegendCheckEventResponse
        {
            data = new()
            {
                chara_info = charaInfo,
                home_info = homeInfo,
                unchecked_event_array = uncheckedEventArray ?? [],
                race_start_info = raceStartInfo,
                legend_data_set = dataSet,
            }
        };

        var gameStatusToSend = new GameStatusSend_Legend(response);
        if (gameStatusToSend.islegal)
            gameStatusToSend.doSend();

        return ValueTask.CompletedTask;
    }
}
