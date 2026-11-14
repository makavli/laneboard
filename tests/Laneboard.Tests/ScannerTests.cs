using System.Diagnostics;
using System.IO;
using Laneboard;
using Xunit;

public class ScannerTests
{
    static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    [Fact]
    public async Task A_command_planted_in_a_repos_own_config_never_runs()
    {
        // An unpacked archive brings its own .git/config; plain `git status` would run core.fsmonitor through the shell.
        var dir = Directory.CreateTempSubdirectory("laneboard-fsm-").FullName;
        var marker = dir + ".ran";
        try
        {
            Git(dir, "init", "-q");
            Git(dir, "config", "core.fsmonitor", $"echo ran > '{marker.Replace('\\', '/')}'");
            var info = await Scanner.Read(dir);
            Assert.Null(info.Error);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task A_moved_repository_says_so_instead_of_git_not_found()
    {
        var info = await Scanner.Read(Path.Combine(Path.GetTempPath(), "laneboard-gone-" + Guid.NewGuid().ToString("N")));
        Assert.StartsWith("Folder not found", info.Error);
    }
}
