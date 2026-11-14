using System.IO;
using Laneboard;
using Xunit;

public class WatcherTests
{
    [Theory]
    [InlineData(@"src\app.cs", false)]
    [InlineData(@".git\HEAD", false)]
    [InlineData(@".git\refs\heads\main", false)]
    [InlineData(@".git\FETCH_HEAD", false)]
    [InlineData(@".git\index", true)]
    [InlineData(@".git\index.lock", true)]
    [InlineData(@".git\objects\ab\cdef", true)]
    [InlineData(@".git\logs\HEAD", true)]
    [InlineData(@"web\node_modules\x\y.js", true)]
    [InlineData(@"src\Game\bin\Debug\a.dll", true)]
    [InlineData(@"Intermediate\Build\x.obj", true)]
    [InlineData("docs/readme.md", false)]
    public void Ignores_churn_that_changes_nothing_shown(string rel, bool ignored) =>
        Assert.Equal(ignored, RepoWatcher.Ignored(rel));

    [Fact]
    public void A_file_change_marks_its_repo_once_until_read()
    {
        var repo = Directory.CreateTempSubdirectory("laneboard-watch-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            using var w = new RepoWatcher();
            int events = 0;
            using var seen = new ManualResetEventSlim();
            w.Changed += () => { Interlocked.Increment(ref events); seen.Set(); };
            w.Sync([repo]);

            File.WriteAllText(Path.Combine(repo, "a.txt"), "1");
            Assert.True(seen.Wait(TimeSpan.FromSeconds(5)), "no event for a new file");
            File.WriteAllText(Path.Combine(repo, "b.txt"), "2");
            Thread.Sleep(300);

            Assert.Equal(1, Volatile.Read(ref events)); // one event per repo, not per write
            Assert.Equal([repo], w.TakeDirty());
            Assert.Empty(w.TakeDirty());

            seen.Reset();
            File.WriteAllText(Path.Combine(repo, "c.txt"), "3"); // after reading, the next change raises again
            Assert.True(seen.Wait(TimeSpan.FromSeconds(5)), "no event after the dirty list was taken");
        }
        finally { Directory.Delete(repo, true); }
    }

    [Fact]
    public void Ignored_changes_raise_nothing()
    {
        var repo = Directory.CreateTempSubdirectory("laneboard-watch-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git", "objects"));
            using var w = new RepoWatcher();
            int events = 0;
            w.Changed += () => Interlocked.Increment(ref events);
            w.Sync([repo]);
            File.WriteAllText(Path.Combine(repo, ".git", "objects", "x"), "1");
            File.WriteAllText(Path.Combine(repo, ".git", "index"), "1");
            Thread.Sleep(800);
            Assert.Equal(0, Volatile.Read(ref events));
        }
        finally { Directory.Delete(repo, true); }
    }
}
