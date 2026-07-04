using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    [ResponseAnalyzer<GameApi.SingleModeLegend.ChangeShortCut>(2)]
    public static ValueTask AnalyzeLegendChangeShortCut(SingleModeLegendChangeShortCutResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.Load>(2)]
    public static ValueTask AnalyzeLegendLoad(SingleModeLegendLoadResponse response)
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

    [ResponseAnalyzer<GameApi.SingleModeLegend.CheckEvent>(2)]
    public static ValueTask AnalyzeLegend(SingleModeLegendCheckEventResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array,
                data.race_start_info)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.CmEnd>(2)]
    public static ValueTask AnalyzeLegendCmEnd(SingleModeLegendCmEndResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.Continue>(2)]
    public static ValueTask AnalyzeLegendContinue(SingleModeLegendContinueResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array,
                data.race_start_info)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.ExecCommand>(2)]
    public static ValueTask AnalyzeLegendExecCommand(SingleModeLegendExecCommandResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.FinishClawCrane>(2)]
    public static ValueTask AnalyzeLegendFinishClawCrane(SingleModeLegendFinishClawCraneResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.GainSkills>(2)]
    public static ValueTask AnalyzeLegendGainSkills(SingleModeLegendGainSkillsResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.LegendRaceContinue>(2)]
    public static ValueTask AnalyzeLegendRaceContinue(SingleModeLegendLegendRaceContinueResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                raceStartInfo: data.race_start_info)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.LegendRaceEnd>(2)]
    public static ValueTask AnalyzeLegendRaceEnd(SingleModeLegendLegendRaceEndResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.LegendRaceEntry>(2)]
    public static ValueTask AnalyzeLegendRaceEntry(SingleModeLegendLegendRaceEntryResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set,
                raceStartInfo: data.race_start_info)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.LegendRaceOut>(2)]
    public static ValueTask AnalyzeLegendRaceOut(SingleModeLegendLegendRaceOutResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.LegendRaceStart>(2)]
    public static ValueTask AnalyzeLegendRaceStart(SingleModeLegendLegendRaceStartResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                homeInfo: null,
                data.legend_data_set,
                raceStartInfo: data.race_start_info)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.PopularityEnd>(2)]
    public static ValueTask AnalyzeLegendPopularityEnd(SingleModeLegendPopularityEndResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.RaceEnd>(2)]
    public static ValueTask AnalyzeLegendCareerRaceEnd(SingleModeLegendRaceEndResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.RaceEntry>(2)]
    public static ValueTask AnalyzeLegendCareerRaceEntry(SingleModeLegendRaceEntryResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array,
                data.race_start_info)
            : ValueTask.CompletedTask;

    [ResponseAnalyzer<GameApi.SingleModeLegend.RaceOut>(2)]
    public static ValueTask AnalyzeLegendCareerRaceOut(SingleModeLegendRaceOutResponse response)
        => response.data is { } data
            ? AnalyzeLegendResponse(
                data.chara_info,
                data.home_info,
                data.legend_data_set,
                data.unchecked_event_array)
            : ValueTask.CompletedTask;
}
