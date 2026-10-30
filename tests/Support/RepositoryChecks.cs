using System.Diagnostics;
using System.Runtime.CompilerServices;

static class RepositoryChecks
{
    public static void Run([CallerFilePath] string entryPoint = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entryPoint)!, "..", ".."));
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(root, "tests", "Support", "VerifyPublicationBoundary.ps1"), "-PluginRoot", root })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Repository publication boundary check failed ({process.ExitCode}): {root}");
    }
}
