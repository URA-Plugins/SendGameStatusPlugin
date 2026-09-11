# SendGameStatusPlugin

将育成回合状态转换为供本机 AI 程序读取的 JSON 文件。当前处理 L'Arc、U.A.F.、大丰食祭、机械、传说、温泉和拉面剧本，状态内容包括回合、基础属性、体力、干劲、技能点、支援卡与训练分布，以及对应剧本的附加数据。

## 输入与输出

插件读取 UmamusumeResponseAnalyzer 捕获的育成响应，并依赖 EventLoggerPlugin 提供回合记录。只在响应包含可用的育成主页状态时输出；待处理事件、比赛中间状态和重复回合等情况由各剧本分析器决定是否输出。

JSON 写入 `PluginData/SendGameStatusPlugin/`：`thisTurn.json` 保存最近一次状态，`game<育成ID>_turn<回合>.json` 保存首次输出；同一育成、同一回合的后续输出依次添加 `_2`、`_3` 等序号。回合从 `0` 开始，序号计数仅在进程内保存。文件通过临时文件替换方式写入；连续 10 次写入失败时抛出 `IOException`。

拉面剧本使用 `scenarioId=14`。`baseGame.source` 区分 `load`、`command`、`event` 和 `special`；命令响应的 `playing_state >= 10` 记为 `special`，中间命令状态跳过输出。RamenScenarioAnalyzer 可用时读取其进程内快照补充地区、诀窍基础增量与检查点分数，快照缺失不阻止输出。`last_ramen=-1` 表示尚未品尝。

插件没有可配置项，也不连接、检测或启动 AI 进程。AI 未运行或未读取这些文件时，插件仍会生成 JSON，但不会产生 AI 计算或回传结果。

## 构建

仓库通过 NuGet 包引用 Host API，通过 Git submodule 固定 EventLoggerPlugin 与 RamenScenarioAnalyzer 源码。克隆后在仓库根执行：

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\SendGameStatusPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
```

Host-dependent smoke 位于 `tests/SendGameStatusPluginSmoke`。

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
