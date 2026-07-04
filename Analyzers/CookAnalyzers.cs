using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    [ResponseAnalyzer<GameApi.SingleModeCook.CheckEvent>(2)]
    public static ValueTask AnalyzeCook(SingleModeCookCheckEventResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, data.race_start_info, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_Cook(response);
        if (gameStatusToSend.islegal)
            GameStatusOutput.WriteScenarioData(gameStatusToSend, gameStatusToSend.turn);
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeCook.ExecCommand>(2)]
    public static ValueTask AnalyzeCookExecCommand(SingleModeCookExecCommandResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, null, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_Cook(response);
        if (gameStatusToSend.islegal)
            GameStatusOutput.WriteScenarioData(gameStatusToSend, gameStatusToSend.turn);
        return ValueTask.CompletedTask;
    }
}
