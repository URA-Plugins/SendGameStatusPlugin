using Gallop;
using UmamusumeResponseAnalyzer.Plugin;

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin : IPlugin
{
    IPluginContext? pluginContext;

    const string RamenCommandEndpointRegex =
        "^/umamusume/single_mode_ramen/(?:change_short_cut|exec_command|finish_claw_crane|gain_skills|race_end|race_entry|race_out|ramen_live|select_region|tasting|uraf_effect_apply)$";

    const string RamenLoadEndpoint = "/umamusume/single_mode_ramen/load";

    const string RamenCheckEventEndpoint = "/umamusume/single_mode_ramen/check_event";

    public void Initialize(IPluginContext context)
    {
        pluginContext = context;
        GameStatusOutput.Configure(Path.Combine("PluginData", "SendGameStatusPlugin"));

        // Legend 维持原 priority=2 注册。
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

        // 拉面杯：注册 3 种 response（Load / ExecCommand / CheckEvent），统一走中间类型 RamenResponse，再交给 AnalyzeRamen。
        // 注意，由于重构的原因，其他没有列出的剧本, Analyzer属于断开状态，无法发送数据。
        RegisterAnalyzer<SingleModeRamenLoadResponse>(
            EndpointPattern.Exact(RamenLoadEndpoint),
            resp => AnalyzeRamen(RamenResponse.FromLoad(resp)));
        RegisterAnalyzer<SingleModeRamenExecCommandResponse>(
            EndpointPattern.Regex(RamenCommandEndpointRegex),
            resp => AnalyzeRamen(RamenResponse.FromExecCommand(resp)));
        RegisterAnalyzer<SingleModeRamenCheckEventResponse>(
            EndpointPattern.Exact(RamenCheckEventEndpoint),
            resp => AnalyzeRamen(RamenResponse.FromCheckEvent(resp)));
    }

    /// <summary>
    /// 注册单个 response 类型的 Analyzer；handler 在 Initialize 之后由插件实例负责调用。
    /// priority 固定为 2，保证 RamenScenarioAnalyzer (priority=1) 先跑、先写状态。
    /// </summary>
    public void RegisterAnalyzer<TResponse>(
        EndpointPattern pattern,
        Func<TResponse, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        var context = pluginContext
            ?? throw new InvalidOperationException(
                "SendGameStatusPlugin.RegisterAnalyzer 必须在 Initialize 之后调用。");

        context.Analyzers.Register<TResponse>(
            AnalyzerKind.Response,
            [pattern],
            invocation => handler(invocation.Payload),
            priority: 2);
    }
}
