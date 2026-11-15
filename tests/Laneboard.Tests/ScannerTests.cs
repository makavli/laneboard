using System.Diagnostics;
using System.IO;
using Laneboard;
using Xunit;

public class ScannerTests
{
    static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o.Trim();
    }

    static string Slashed(string path) => path.Replace('\\', '/');

    // git writes its objects read-only, which Directory.Delete refuses.
    static void Wipe(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task A_filter_planted_in_a_repos_own_config_never_runs()
    {
        // git status re-reads a file whose timestamp moved, through the clean filter its attributes name.
        var dir = Directory.CreateTempSubdirectory("laneboard-filter-").FullName;
        var marker = dir + ".ran";
        try
        {
            Git(dir, "init", "-q");
            File.WriteAllText(Path.Combine(dir, ".gitattributes"), "*.txt filter=x\n");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "hi\n");
            Git(dir, "add", ".");
            Git(dir, "-c", "user.email=a@b", "-c", "user.name=a", "commit", "-qm", "i");
            Git(dir, "config", "filter.x.clean", $"echo ran > '{Slashed(marker)}'; cat");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "a.txt"), new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var info = await Scanner.Read(dir);
            Assert.StartsWith("Not read", info.Error);
            Assert.Contains("filter.x.clean", info.Error);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Wipe(dir);
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task A_gpg_program_planted_in_a_repos_own_config_never_runs()
    {
        // log.showSignature=true makes `git log` verify a signed commit with gpg.program.
        var dir = Directory.CreateTempSubdirectory("laneboard-gpg-").FullName;
        var marker = dir + ".ran";
        var gpg = dir + ".gpg";
        try
        {
            Git(dir, "init", "-q");
            File.WriteAllText(gpg, $"#!/bin/sh\necho ran > '{Slashed(marker)}'\n");
            var empty = Path.Combine(dir, ".git", "empty");
            File.WriteAllText(empty, "");
            var tree = Git(dir, "hash-object", "-t", "tree", "-w", empty);
            var body = Path.Combine(dir, ".git", "commit");
            File.WriteAllText(body, $"tree {tree}\nauthor a <a@b> 1 +0000\ncommitter a <a@b> 1 +0000\n" +
                "gpgsig -----BEGIN PGP SIGNATURE-----\n \n abc\n -----END PGP SIGNATURE-----\n\nsigned\n");
            Git(dir, "update-ref", "HEAD", Git(dir, "hash-object", "-t", "commit", "-w", body));
            Git(dir, "config", "log.showSignature", "true");
            Git(dir, "config", "gpg.program", Slashed(gpg));
            var info = await Scanner.Read(dir);
            Assert.Null(info.Error);
            Assert.Equal("signed", info.LastSubject);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Wipe(dir);
            File.Delete(marker);
            File.Delete(gpg);
        }
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
