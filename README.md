# SendGameStatusPlugin

将育成回合状态转换为供本机 AI 程序读取的 JSON 文件。当前处理 L'Arc、U.A.F.、大丰食祭、机械、传说和温泉剧本，状态内容包括回合、基础属性、体力、干劲、技能点、支援卡与训练分布，以及对应剧本的附加数据。

## 输入与输出

插件读取 UmamusumeResponseAnalyzer 捕获的育成响应，并依赖 EventLoggerPlugin 提供回合记录。只在响应包含可用的育成主页状态时输出；待处理事件、比赛中间状态和重复回合等情况按各剧本分析器的当前规则跳过。

JSON 写入 `PluginData/SendGameStatusPlugin/<状态类型>/`：`thisTurn.json` 保存最近一次状态，`turn<回合>.json` 保存对应回合。文件通过临时文件替换方式写入；连续 10 次写入失败时抛出 `IOException`。

插件没有可配置项，也不连接、检测或启动 AI 进程。AI 未运行或未读取这些文件时，插件仍会生成 JSON，但不会产生 AI 计算或回传结果。

## 构建

需要 .NET 10 SDK，并保持本仓库与 `EventLoggerPlugin` 处于同一父目录。在仓库根目录运行：

```powershell
dotnet build SendGameStatusPlugin.csproj -p:GenerateUraPluginManifestOnBuild=false -p:PackageUraPluginOnBuild=false -p:DeployUraPluginToLocalAppDataOnBuild=false
```
