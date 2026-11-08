# SendGameStatusPlugin

将育成回合状态转换为供本机 AI 程序读取的 JSON 文件。当前处理 L'Arc、U.A.F.、大丰食祭、机械、传说、温泉、拉面和骏大祭剧本，状态内容包括回合、基础属性、体力、干劲、技能点、支援卡与训练分布，以及对应剧本的附加数据。

## 输入与输出

插件读取 UmamusumeResponseAnalyzer 捕获的育成响应，并依赖 EventLoggerPlugin 提供回合记录。只在响应包含可用的育成主页状态时输出；待处理事件、比赛中间状态和重复回合等情况由各剧本分析器决定是否输出。

JSON 写入 `PluginData/SendGameStatusPlugin/`：`thisTurn.json` 保存最近一次状态，`game<育成ID>_turn<回合>.json` 保存首次输出；同一育成、同一回合的后续输出依次添加 `_2`、`_3` 等序号。回合从 `0` 开始，序号计数仅在进程内保存。文件通过临时文件替换方式写入；连续 10 次写入失败时抛出 `IOException`。

拉面剧本使用 `scenarioId=14`。`baseGame.source` 区分 `load`、`command`、`event` 和 `special`；命令响应的 `playing_state >= 10` 记为 `special`，中间命令状态跳过输出。RamenScenarioAnalyzer 可用时读取其进程内快照补充地区、诀窍基础增量与检查点分数，快照缺失不阻止输出。`last_ramen=-1` 表示尚未品尝。

骏大祭剧本使用 `scenarioId=15`，剧本段名为 `festa`。字段尽量原样转发协议，换算留给 matsuri：`acts`（演目 `act_type`/`gauge`/`level`/`need_act_gauge`、教诲 `advice_id` 与转成 `fusion_advice_id`）、`maxActLevel`、`campRemain`、`checkPoints`（`result_type` 原值）、`persons`（担当名单：`target_id` + `actType` + `origin`，`origin` 取 `card`/`friend`/`guest`/`yayoi`/`reporter`；`target_id` 已换算到与 `personDistribution` 相同的编号空间，只发有演目归属的成员（`actType > 0`）。演目归属只有这里能表达，故不进 `baseGame`）、`kensan`（逐训练位追加稽古标记）、`assistSupportLeft`（状态 47 存在时为 `1`，否则 `0`；语义仅为「有无」，**不是剩余次数**——协议不给次数，插件侧也不维护计数器，需要次数时由消费端自行累计）与 `fatigueRecovery`（状态 45）。`trainLevelTotal` 是 5 个训练位的等级（按 速/耐/力/根/智 排序）。只有 `load` / `command` 帧的协议值可信（load 是整局快照，数据更全）：当帧有基础训练项就用当帧值并更新基准，拿不到（合宿期、终盘只能稽古）就用最近一次观测值补齐；事件帧与结算帧（`source=event`/`special`）整组置 0，因为协议在那类响应里把基础项 `level` 填成默认 1、不可信。逐位 `0` 表示整局尚无观测值。稽古 5101–5105 的 `level` 恒为 1、是稽古自身等级，不参与训练位计算。`baseGame.trainLevelCount` 则存 EventLogger 累计的**成功普通训练次数**——两笔账必须分开，否则会把「离下一级还差 4 次」判错。

通用段的 `baseGame.training` 给出本回合各训练指令的游戏侧数值，是**协议算好的最终结果**，供 AI 端与自己算的比对：key 为指令 id（`101`–`106`、`601`–`605` 转成、`5101`–`5105` 稽古等剧本专属 id 原样保留，映射交给消费方），每项含 `failureRate` 与 `lower`/`upper` 两个 **7 项定长数组**，顺序与剧本 `training.normal` 基础值一致：速 / 耐 / 力 / 根 / 智 / 技能点 / 体力（负数为消耗）。`lower` 取 `home_info.command_info_array`（游戏里的下层），`upper` 取 `festa_data_set.command_info_array`（剧本加成，游戏里的上层），逐项相加即总增量。数值为协议原值，**未做 1200 衰减**（与 `fiveStatus` 同口径）。只列 `command_type == 1 && is_enable == 1` 的指令，不可训练回合为空对象 `{}`；该字段目前仅骏大祭填充，其余剧本为 `null` 不输出。

骏大祭的帧依赖 FestaScenarioAnalyzer 的进程内快照（`FestaScenarioState.Snapshot`）：快照的 `CharaId`/`Turn` 与当前响应不一致时整帧跳过，不发出拼接错乱的帧；`change_short_cut`、`finish_claw_crane` 不带 `home_info`，不注册。`personDistribution` 与 `lockedTrainingId` 对骏大祭不可信（`ToTrainIndex` 没有稽古指令 5101–5105），改以快照的训练位参与者重建、锁定位置写 `-1`。人头沿用 BaseGame 的既有约定（卡位 1–6 → 0–5、理事长 102 → 6、记者 103 → 7），**只有 NPC（≥1000）写协议原始 id**，不再压成 `8`，否则身份会丢；每个训练位最多 5 人，超出截断。`baseGame.persons[i].charaId` 由支援卡表补真实角色 id（NPC 的 `target_id` 本身即角色 id）；`festa.persons` 沿用同一套人头编号（卡位 0–5、理事长 6、记者 7、NPC 原始 id），不带 charaId，可以直接用分布里的值来查。

终局帧：育成结束（`chara_info.state` 为 2 或 3、且无待处理事件）时，另外单独写一份 `game<育成ID>_finalScore.json`（扁平结构，**不写** `thisTurn.json`，同一育成只写第一份）。判据与端点用通用 `single_mode*/check_event`、`single_mode*/load` 注册，全剧本共用。`game<育成ID>_final.json` 是记录器把该帧原文归档进局包时另起的名字，两者不是同一份文件。

终局帧在原有终局字段之外，顶层增加两个字段：

- `actionSchemaVersion`：动作段结构版本，当前为 `1`。消费端靠它区分带采集的新帧与历史老帧；历史局包无此字段时读不到动作应回退到状态差推断，不能当作「玩家没动作」。
- `actions`：玩家实际动作序列，**即使为空也输出 `[]`**。`exec_command` / `check_event` / `race_entry` 三类请求在进程内按到达顺序累积（宿主 `dispatchGate` 串行派发，数组下标即真实点击顺序），只有正常终局才一次性落盘；全程不写中间文件、不进决策链路。

`actions[]` 元素为 `turn`（**0 基**，`= current_turn - 1`，与 `game<育成ID>_turn<回合>.json` 同口径）、`endpoint`（端点尾段短名）加上请求 `xxx_request_common` 的全部 public 字段按协议原名（snake_case）展平，例如 `command_type` / `command_id` / `event_id` / `choice_number` / `program_id`；`current_turn` 已由 `turn` 承载，不重复落盘。只记协议原值，**不做语义分类**（`command_type` 取值表待真机采样后再定）。`RequestId` 只用作进程内去重键，不写入 JSON。`actions` 的语义是「本次插件进程内捕获到的动作序列」：中途加载或断线重连的前半局天然缺失，消费方不得把某回合没有动作解读成玩家没操作。终局帧只写第一份，之后结算画面的请求不再记入。

### 单选项自动事件过滤

`check_event` 请求里的 `choice_number=0` 有两种含义：玩家在多选项里主动选了第 0 项，或该事件只有一个选项、客户端自动确认。请求侧只有 `event_id / choice_number`，**无法区分**，因此不能按 `choice_number == 0` 硬过滤（真机 game6310 里 `10002` 出现过 `{0,1,2}`、`10000` 出现过 `{0,1}`，它们的 0 是真实选择）。

插件从响应侧 `unchecked_event_array[].event_contents_info.choice_array.Length` 收集 `event_id → 选项数`，整局按 `event_id` 取**最大值**（某个 ID 只要见过一次多选项，就永不判成自动事件）；终局 `TakeSnapshot()` 时丢弃「见过的最大选项数 ≤ 1」的 `check_event`。`choice_array` 为 null（协议没给）的 `event_id` 一律**保留**——宁可漏过滤，也不误删真实选择。切局与取快照时清空观察表。

观察来源为四个通用响应：`check_event` / `load`（现有终局判据注册里顺带取）以及新增的 `exec_command` / `continue` 两个 wildcard 注册（覆盖「指令或赛程之后弹出一串事件」的串首）。新增注册只喂内存观察表，**不写任何文件、不动 `TurnSequence`、不进决策链路**；各剧本响应的 `unchecked_event_array` 层级一致，通用 DTO 对剧本专属的 `*_data_set` 字段由 `GallopMessagePackFormatter` 跳过未知 key，可安全复用。

⚠️ **待真机验证**：判定前提是「单选项自动事件确实出现在 `unchecked_event_array` 里」。第一局请对比 `actions` 中 `check_event` 的条数（game6310 改版前为 193 条，其中 160 条 `choice_number=0`、97 个 `event_id` 整局恒为 0）。若过滤后仍接近 193，说明自动事件不进该列表，需换判定源（按 `event_id` 查静态事件表，或退到带误删风险的启发式），**不要**改成按 `choice_number` 硬过滤。

两个 Rust 消费端的终局帧结构没有 `deny_unknown_fields`，这两个顶层字段会被 serde 静默忽略；记录器以**原文**落盘 `game<育成ID>_final.json` 并打包，因此动作数据自动进入局包，未来复盘直接从顶层读 `actions`。`umaai-rs-ref` 的 `on_final` 目前只接纳 `scenarioId=14`，其他剧本的帧仍留在插件目录，是否放宽属消费端调整范围。

注意：上面的归档链路以「消费端已把白名单同步为 `game<育成ID>_finalScore.json`」为前提。本轮**只改插件端**，消费端尚未同步（见 `URA_Plugins/handoff-2026-11-06.md` §7.4），在此之前终局帧不会被 AI 引擎接收、也不会进局包；真机验证请直接检查插件目录下的 `game<育成ID>_finalScore.json`。

插件没有可配置项，也不连接、检测或启动 AI 进程。AI 未运行或未读取这些文件时，插件仍会生成 JSON，但不会产生 AI 计算或回传结果。

## 构建

仓库通过 NuGet 包引用 Host API，通过 Git submodule 固定 EventLoggerPlugin、RamenScenarioAnalyzer 与 FestaScenarioAnalyzer 源码。克隆后在仓库根执行：

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\SendGameStatusPlugin.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
```

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
