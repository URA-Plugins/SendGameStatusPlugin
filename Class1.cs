using Gallop;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin : IPlugin
{
    public void Initialize(IPluginContext context)
    {
        GameStatusOutput.Configure(Path.Combine("PluginData", "SendGameStatusPlugin"));
        context.Analyzers.Register<SingleModeLegendCheckEventResponse>(
            AnalyzerKind.Response,
            [EndpointPattern.Regex(
                "^/umamusume/single_mode_legend/(?:change_short_cut|check_event|cm_end|continue|exec_command|finish_claw_crane|gain_skills|legend_race_continue|legend_race_end|legend_race_entry|legend_race_out|legend_race_start|popularity_end|race_end|race_entry|race_out)$")],
            invocation => AnalyzeLegend(invocation.Payload),
            priority: 2);
        context.Analyzers.Register<SingleModeLegendLoadResponse>(
            AnalyzerKind.Response,
            [EndpointPattern.Exact("/umamusume/single_mode_legend/load")],
            invocation => AnalyzeLegendLoad(invocation.Payload),
            priority: 2);
    }
}
