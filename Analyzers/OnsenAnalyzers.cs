using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin
{
    [ResponseAnalyzer<GameApi.SingleModeOnsen.CheckEvent>]
    public static ValueTask AnalyzeOnsenEvent(SingleModeOnsenCheckEventResponse response)
    {
        if (response.data.unchecked_event_array is null || response.data.unchecked_event_array.Length == 0)
            return ValueTask.CompletedTask;

        foreach (var ev in response.data.unchecked_event_array)
        {
            if (ev.event_contents_info.choice_array.Length < 2 || !Database.Events.ContainsKey(ev.story_id))
                continue;

            var gameStatusToSend = new GameStatusSend_Onsen(response);
            gameStatusToSend.doSend();
        }

        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeOnsen.CheckEvent>(2)]
    public static ValueTask AnalyzeOnsen(SingleModeOnsenCheckEventResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, data.race_start_info, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_Onsen(response);
        if (gameStatusToSend.baseGame.islegal)
            gameStatusToSend.doSend();
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeOnsen.ExecCommand>(2)]
    public static ValueTask AnalyzeOnsenExecCommand(SingleModeOnsenExecCommandResponse response)
    {
        var data = response.data;
        if (ShouldSkip(data.home_info, data.unchecked_event_array, null, data.chara_info.state))
            return ValueTask.CompletedTask;

        var gameStatusToSend = new GameStatusSend_Onsen(response);
        if (gameStatusToSend.baseGame.islegal)
            gameStatusToSend.doSend();
        return ValueTask.CompletedTask;
    }
}
