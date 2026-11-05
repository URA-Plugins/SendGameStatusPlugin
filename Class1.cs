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

    // 骏大祭：change_short_cut、finish_claw_crane 不带 home_info，无法构造 base 段，故不注册。
    // fusion_advice 没有育成身份与训练信息，等下一份完整响应。
    const string FestaCommandEndpointRegex =
        "^/umamusume/single_mode_festa/(?:attach_advice|check_event|check_point|continue|festa_live|gain_skills|race_entry|race_out)$";

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
        RegisterAnalyzer<SingleModeRamenLoadResponse>(
            EndpointPattern.Exact(RamenLoadEndpoint),
            resp => AnalyzeRamen(RamenResponse.FromLoad(resp)));
        RegisterAnalyzer<SingleModeRamenExecCommandResponse>(
            EndpointPattern.Regex(RamenCommandEndpointRegex),
            resp => AnalyzeRamen(RamenResponse.FromExecCommand(resp)));
        RegisterAnalyzer<SingleModeRamenCheckEventResponse>(
            EndpointPattern.Exact(RamenCheckEventEndpoint),
            resp => AnalyzeRamen(RamenResponse.FromCheckEvent(resp)));

        // 骏大祭：4 种 response（Load / Start / ExecCommand / CheckEvent），
        // 统一走中间类型 FestaResponse，再交给 AnalyzeFesta。
        RegisterAnalyzer<SingleModeFestaLoadResponse>(
            EndpointPattern.Exact("/umamusume/single_mode_festa/load"),
            resp => AnalyzeFesta(FestaResponse.FromLoad(resp)));
        RegisterAnalyzer<SingleModeFestaStartResponse>(
            EndpointPattern.Exact("/umamusume/single_mode_festa/start"),
            resp => AnalyzeFesta(FestaResponse.FromStart(resp)));
        RegisterAnalyzer<SingleModeFestaExecCommandResponse>(
            EndpointPattern.Exact("/umamusume/single_mode_festa/exec_command"),
            resp => AnalyzeFesta(FestaResponse.FromExecCommand(resp)));
        RegisterAnalyzer<SingleModeFestaCheckEventResponse>(
            EndpointPattern.Regex(FestaCommandEndpointRegex),
            resp => AnalyzeFesta(FestaResponse.FromCheckEvent(resp)));

        // 终局帧：育成结束·点技能前单独写一份 finalScore.json。用通用 response 类型 +
        // wildcard（同 SkillTipsResponseAnalyzer），全剧本共用；是否解析由 umaai 侧按
        // scenarioId 决定。终局帧不进决策链路，故独立于上面的回合快照注册。
        RegisterAnalyzer<SingleModeCheckEventResponse>(
            EndpointPattern.Wildcard("/umamusume/single_mode*/check_event"),
            resp => AnalyzeGameEnd(resp.data?.chara_info, resp.data?.unchecked_event_array));
        RegisterAnalyzer<SingleModeLoadResponse>(
            EndpointPattern.Wildcard("/umamusume/single_mode*/load"),
            resp => AnalyzeGameEnd(
                resp.data?.single_mode_load_common?.chara_info,
                resp.data?.single_mode_load_common?.unchecked_event_array));
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
