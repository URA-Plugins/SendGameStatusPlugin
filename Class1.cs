using System.IO.Compression;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using UmamusumeResponseAnalyzer.Plugin;

// 与共享库 EventLoggerPlugin 同组，整组共用一个 collectible ALC
[assembly: SharedContextWith("EventLoggerPlugin")]

namespace SendGameStatusPlugin;

public partial class SendGameStatusPlugin : IPlugin
{
    public string Name => "SendGameStatusPlugin";
    public string Author => "UmaAi Team";
    public string[] Targets => [];
    public string DataDirectory => Path.Combine("PluginData", Name);

    public void Initialize(IPluginContext context)
    {
        GameStatusOutput.Configure(DataDirectory);
    }

    public async Task UpdatePlugin(ProgressContext ctx)
    {
        var progress = ctx.AddTask($"[[{Name}]] 更新");

        using var client = new HttpClient();
        using var resp = await client.GetAsync($"https://api.github.com/repos/URA-Plugins/{Name}/releases/latest");
        var jo = JObject.Parse(await resp.Content.ReadAsStringAsync());

        var isLatest = ("v" + ((IPlugin)this).Version).Equals("v" + jo["tag_name"]?.ToString());
        if (isLatest)
        {
            progress.Increment(progress.MaxValue);
            progress.StopTask();
            return;
        }
        progress.Increment(25);

        var downloadUrl = jo["assets"]![0]!["browser_download_url"]!.ToString();
        using var msg = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        var contentLength = msg.Content.Headers.ContentLength ?? 0;

        using var memoryStream = new MemoryStream();
        await using (var stream = await msg.Content.ReadAsStreamAsync())
        {
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                memoryStream.Write(buffer, 0, read);
                if (contentLength > 0)
                    progress.Increment((double)read / contentLength * 50);
            }
             if (@event.IsScenario(ScenarioType.Legend))
 {
     if (GameStatusSend_Legend.GetCommandInfoStage_legend(@event) == 5 || GameStatusSend_Legend.GetCommandInfoStage_legend(@event) == 3)
     {
         //收录了效果，可以发给AI分析，分别是选心得事件和老登三选一事件
         AnsiConsole.MarkupLine("[aqua]检测到老登杯特殊事件，正在发送给AI分析...[/]");
         var gameStatusToSend = new GameStatusSend_Legend(@event);
        gameStatusToSend.doSend();
         
        
     }
 }
        }

        memoryStream.Position = 0;
        using var archive = new ZipArchive(memoryStream);
        archive.ExtractToDirectory(Path.Combine("Plugins", Name), true);
        progress.Increment(25);

        progress.StopTask();
    }
}
