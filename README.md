# SendGameStatusPlugin

将育成回合状态转换为供本机 AI 程序读取的 JSON 文件。当前处理 L'Arc、U.A.F.、大丰食祭、机械、传说、温泉、拉面和骏大祭剧本，状态内容包括回合、基础属性、体力、干劲、技能点、支援卡与训练分布，以及对应剧本的附加数据。

## 输入与输出

插件读取 UmamusumeResponseAnalyzer 捕获的育成响应，并依赖 EventLoggerPlugin 提供回合记录。只在响应包含可用的育成主页状态时输出；待处理事件、比赛中间状态和重复回合等情况由各剧本分析器决定是否输出。

JSON 写入 `PluginData/SendGameStatusPlugin/`：`thisTurn.json` 保存最近一次状态，`game<育成ID>_turn<回合>.json` 保存首次输出；同一育成、同一回合的后续输出依次添加 `_2`、`_3` 等序号。回合从 `0` 开始，序号计数仅在进程内保存。文件通过临时文件替换方式写入；连续 10 次写入失败时抛出 `IOException`。

拉面剧本使用 `scenarioId=14`。`baseGame.source` 区分 `load`、`command`、`event` 和 `special`；命令响应的 `playing_state >= 10` 记为 `special`，中间命令状态跳过输出。RamenScenarioAnalyzer 可用时读取其进程内快照补充地区、诀窍基础增量与检查点分数，快照缺失不阻止输出。`last_ramen=-1` 表示尚未品尝。

骏大祭剧本使用 `scenarioId=15`，剧本段名为 `festa`。字段尽量原样转发协议，换算留给 matsuri：`acts`（演目 `act_type`/`gauge`/`level`/`need_act_gauge`、教诲 `advice_id` 与转成 `fusion_advice_id`）、`maxActLevel`、`campRemain`、`checkPoints`（`result_type` 原值）、`persons`（担当名单：协议原始 `target_id` + `actType` + `origin`，`origin` 取 `card`/`friend`/`guest`/`yayoi`/`reporter`。演目归属只有这里能表达，故不进 `baseGame`）、`kensan`（逐训练位追加稽古标记）、`assistSupportLeft`（状态 47 存在时为 `1`，否则 `0`；语义仅为「有无」，**不是剩余次数**——协议不给次数，插件侧也不维护计数器，需要次数时由消费端自行累计）与 `fatigueRecovery`（状态 45）。`trainLevelTotal` 是协议总等级（含発表会 +1），`baseGame.trainLevelCount` 则存 EventLogger 累计的**成功普通训练次数**——两笔账必须分开，否则会把「离下一级还差 4 次」判错。

通用段的 `baseGame.training` 给出本回合各训练指令的游戏侧数值，是**协议算好的最终结果**，供 AI 端与自己算的比对：key 为指令 id（`101`–`106`、`601`–`605` 转成、`5101`–`5105` 稽古等剧本专属 id 原样保留，映射交给消费方），每项含 `failureRate` 与 `lower`/`upper` 两个 **7 项定长数组**，顺序与剧本 `training.normal` 基础值一致：速 / 耐 / 力 / 根 / 智 / 技能点 / 体力（负数为消耗）。`lower` 取 `home_info.command_info_array`（游戏里的下层），`upper` 取 `festa_data_set.command_info_array`（剧本加成，游戏里的上层），逐项相加即总增量。数值为协议原值，**未做 1200 衰减**（与 `fiveStatus` 同口径）。只列 `command_type == 1 && is_enable == 1` 的指令，不可训练回合为空对象 `{}`；该字段目前仅骏大祭填充，其余剧本为 `null` 不输出。

骏大祭的帧依赖 FestaScenarioAnalyzer 的进程内快照（`FestaScenarioState.Snapshot`）：快照的 `CharaId`/`Turn` 与当前响应不一致时整帧跳过，不发出拼接错乱的帧；`change_short_cut`、`finish_claw_crane` 不带 `home_info`，不注册。`personDistribution` 与 `lockedTrainingId` 对骏大祭不可信（`ToTrainIndex` 没有稽古指令 5101–5105），改以快照的训练位参与者重建、锁定位置写 `-1`。人头沿用 BaseGame 的既有约定（卡位 1–6 → 0–5、理事长 102 → 6、记者 103 → 7），**只有 NPC（≥1000）写协议原始 id**，不再压成 `8`，否则身份会丢；每个训练位最多 5 人，超出截断。`baseGame.persons[i].charaId` 由支援卡表补真实角色 id（NPC 的 `target_id` 本身即角色 id）；`festa.persons` 只给协议原始 id，不带 charaId。

终局帧：育成结束（`chara_info.state` 为 2 或 3、且无待处理事件）时，另外单独写一份 `finalScore.json`（固定名、扁平结构、只含五维/上限/剩余技能点/继承增量等终局数据，**不写** `thisTurn.json`，同一育成只写第一份）。判据与端点用通用 `single_mode*/check_event`、`single_mode*/load` 注册，全剧本共用；umaai 侧目前只消费 `scenarioId=14`。

插件没有可配置项，也不连接、检测或启动 AI 进程。AI 未运行或未读取这些文件时，插件仍会生成 JSON，但不会产生 AI 计算或回传结果。

## 构建

仓库通过 NuGet 包引用 Host API，通过 Git submodule 固定 EventLoggerPlugin、RamenScenarioAnalyzer 与 FestaScenarioAnalyzer 源码。克隆后在仓库根执行：

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\SendGameStatusPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
```

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
