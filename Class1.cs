using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.IO.Compression;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;

[assembly: SharedContextWith("SkillTipsResponseAnalyzer")]
namespace SendGameStatusPlugin
{
    public class SendGameStatusPlugin : IPlugin
    {
        [PluginDescription("向AI发送游戏信息")]
        public string Name => "SendGameStatusPlugin";
        public string Author => "UmaAi Team";
        public Version Version => new(1, 0, 0);
        public string[] Targets => [];
        public async Task UpdatePlugin(ProgressContext ctx)
        {
            var progress = ctx.AddTask($"[[{Name}]] 更新");

            using var client = new HttpClient();
            using var resp = await client.GetAsync($"https://api.github.com/repos/URA-Plugins/{Name}/releases/latest");
            var json = await resp.Content.ReadAsStringAsync();
            var jo = JObject.Parse(json);

            var isLatest = ("v" + Version.ToString()).Equals("v" + jo["tag_name"]?.ToString());
            if (isLatest)
            {
                progress.Increment(progress.MaxValue);
                progress.StopTask();
                return;
            }
            progress.Increment(25);

            var downloadUrl = jo["assets"][0]["browser_download_url"].ToString();
            if (Config.Updater.IsGithubBlocked && !Config.Updater.ForceUseGithubToUpdate)
            {
                downloadUrl = downloadUrl.Replace("https://", "https://gh.shuise.dev/");
            }
            using var msg = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await msg.Content.ReadAsStreamAsync();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;
                progress.Increment(read / msg.Content.Headers.ContentLength ?? 1 * 0.5);
            }
            using var archive = new ZipArchive(stream);
            archive.ExtractToDirectory(Path.Combine("Plugins", Name), true);
            progress.Increment(25);

            progress.StopTask();
        }
        /// 额外把事件数据发给AI分析
        [Analyzer]
        public static void AnalyzeEvent(JObject jo)
        {
            if (jo["data"] is null || jo["data"] is not JObject data) return;
            var @event = jo.ToObject<Gallop.SingleModeCheckEventResponse>();
            if (@event is null || @event.data.unchecked_event_array is null) return;
            if (@event.data.unchecked_event_array.Length == 0) return;
            // 这里其实有剧本和事件信息了。
            // 检查事件选项数量是否>=2 且需要玩家选择（这时肯定不是训练）
            foreach (var ev in @event.data.unchecked_event_array)
            {
                if (ev.event_contents_info.choice_array.Length >= 2 && Database.Events.ContainsKey(ev.story_id))
                {
                    // 收录了事件效果，可以发给AI分析
                    if (@event.IsScenario(ScenarioType.Onsen) || @event.data.chara_info.scenario_id == 12)
                    {
                        //AnsiConsole.MarkupLine("[aqua]事件效果已收录...[/]");
                        var gameStatusToSend = new GameStatusSend_Onsen(@event);
                        gameStatusToSend.doSend();
                    }
                }
            }
        }
        
        [Analyzer(priority: 2)]
        public static void Analyze(JObject jo)
        {
            if (!jo.HasCharaInfo()) return;
            if (jo["data"] is null || jo["data"] is not JObject data) return;
            if (data["chara_info"] is null || data["chara_info"] is not JObject chara_info) return;
            var state = chara_info["state"].ToInt();
            if (chara_info != null && data["home_info"]?["command_info_array"] != null && data["race_reward_info"].IsNull() && !(state is 2 or 3)) //根据文本简单过滤防止重复、异常输出
            {
                var @event = jo.ToObject<Gallop.SingleModeCheckEventResponse>();
                if ((@event.data.unchecked_event_array != null && @event.data.unchecked_event_array.Length > 0) || @event.data.race_start_info != null) return;

                if (@event.IsScenario(ScenarioType.LArc))
                {
                    try
                    {
                        var gameStatusToSend = new GameStatusSend_LArc(@event);

                        var currentGSdirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "GameData");
                        Directory.CreateDirectory(currentGSdirectory);

                        var success = false;
                        var tried = 0;
                        do
                        {
                            try
                            {
                                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }; // 去掉空值避免C++端抽风
                                File.WriteAllText($@"{currentGSdirectory}/thisTurn.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                File.WriteAllText($@"{currentGSdirectory}/turn{@event.data.chara_info.turn}.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                success = true; // 写入成功，跳出循环
                                break;
                            }
                            catch
                            {
                                tried++;
                                AnsiConsole.MarkupLine("[yellow]写入失败，0.5秒后重试...[/]");
                                Thread.Sleep(500); // 等待0.5秒
                            }
                        } while (!success && tried < 10);
                        if (!success)
                        {
                            AnsiConsole.MarkupLine($@"[red]写入{currentGSdirectory}/thisTurn.json失败！[/]");
                        }
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.MarkupLine($"[red]向AI发送数据失败！错误信息：{Environment.NewLine}{e.Message}[/]");
                    }
                }
                if (@event.IsScenario(ScenarioType.UAF))
                {
                    try
                    {
                        var gameStatusToSend = new GameStatusSend_UAF(@event);
                        AnsiConsole.MarkupLine("[aqua]AI所需信息已生成...[/]");
                        var currentGSdirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "GameData");
                        Directory.CreateDirectory(currentGSdirectory);

                        var success = false;
                        var tried = 0;
                        do
                        {
                            try
                            {
                                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }; // 去掉空值避免C++端抽风
                                File.WriteAllText($@"{currentGSdirectory}/thisTurn.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                File.WriteAllText($@"{currentGSdirectory}/turn{@event.data.chara_info.turn}.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                success = true; // 写入成功，跳出循环
                                break;
                            }
                            catch
                            {
                                tried++;
                                AnsiConsole.MarkupLine("[yellow]写入失败，0.5秒后重试...[/]");
                                Thread.Sleep(500); // 等待0.5秒
                            }
                        } while (!success && tried < 10);
                        if (!success)
                        {
                            AnsiConsole.MarkupLine($@"[red]写入{currentGSdirectory}/thisTurn.json失败！[/]");
                        }
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.MarkupLine($"[red]向AI发送数据失败！错误信息：{Environment.NewLine}{e.Message}[/]");
                    }
                }
                if (@event.IsScenario(ScenarioType.Cook))
                {
                    var gameStatusToSend = new GameStatusSend_Cook(@event);
                    if (gameStatusToSend.islegal)
                    {
                        var currentGSdirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmamusumeResponseAnalyzer", "GameData");
                        Directory.CreateDirectory(currentGSdirectory);

                        var success = false;
                        var tried = 0;
                        do
                        {
                            try
                            {
                                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }; // 去掉空值避免C++端抽风
                                File.WriteAllText($@"{currentGSdirectory}/thisTurn.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                File.WriteAllText($@"{currentGSdirectory}/turn{@event.data.chara_info.turn}.json", JsonConvert.SerializeObject(gameStatusToSend, Formatting.Indented, settings));
                                success = true; // 写入成功，跳出循环
                                break;
                            }
                            catch
                            {
                                tried++;
                                AnsiConsole.MarkupLine("[yellow]写入失败，0.5秒后重试...[/]");
                                //await Task.Delay(500); // 等待0.5秒
                            }
                        } while (!success && tried < 10);
                        if (!success)
                        {
                            AnsiConsole.MarkupLine($@"[red]写入{currentGSdirectory}/thisTurn.json失败！[/]");
                        }
                    }

                }
                if (@event.IsScenario(ScenarioType.Legend))
                {
                    var gameStatusToSend = new GameStatusSend_Legend(@event);
                    if (gameStatusToSend.islegal)
                    {
                        gameStatusToSend.doSend();
                    }
                }
                if (@event.IsScenario(ScenarioType.Onsen) || @event.data.chara_info.scenario_id == 12)
                {
                    var gameStatusToSend = new GameStatusSend_Onsen(@event);
                    if (gameStatusToSend.baseGame.islegal)
                    {
                        gameStatusToSend.doSend();
                    }
                }
            }
        }
    }
}
