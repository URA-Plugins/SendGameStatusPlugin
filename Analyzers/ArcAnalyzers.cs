using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    [ResponseAnalyzer<GameApi.SingleModeArc.CheckEvent>(2)]
    public static ValueTask AnalyzeLArc(SingleModeArcCheckEventResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, data.race_start_info, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_LArc(response);
        GameStatusOutput.WriteScenarioData(gameStatusToSend, gameStatusToSend.turn);
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeArc.ExecCommand>(2)]
    public static ValueTask AnalyzeLArcExecCommand(SingleModeArcExecCommandResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, null, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_LArc(response);
        GameStatusOutput.WriteScenarioData(gameStatusToSend, gameStatusToSend.turn);
        return ValueTask.CompletedTask;
    }
}
