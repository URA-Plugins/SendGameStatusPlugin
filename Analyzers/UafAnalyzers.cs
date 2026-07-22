using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    [ResponseAnalyzer<GameApi.SingleModeSport.CheckEvent>(2)]
    public static ValueTask AnalyzeUaf(SingleModeSportCheckEventResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, data.race_start_info, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_UAF(response);
        GameStatusOutput.WriteScenarioData(gameStatusToSend, gameStatusToSend.turn);
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeSport.ExecCommand>(2)]
    public static ValueTask AnalyzeUafExecCommand(SingleModeSportExecCommandResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, null, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_UAF(response);
        GameStatusOutput.WriteScenarioData(gameStatusToSend, gameStatusToSend.turn);
        return ValueTask.CompletedTask;
    }
}
