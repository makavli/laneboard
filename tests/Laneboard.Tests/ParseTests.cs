using System.IO;
using System.Text.Json;
using Laneboard;
using Xunit;

public class ParseTests
{
    [Fact]
    public void Porcelain_reads_branch_ahead_behind_and_changes()
    {
        var s = Parse.PorcelainV2(
            "# branch.oid abc\n# branch.head feat/x\n# branch.upstream origin/feat/x\n# branch.ab +2 -1\n" +
            "1 .M N... 100644 100644 100644 a b src/a.cs\n? new.txt\n");
        Assert.Equal(new Parse.GitStatus("feat/x", 2, 1, 2, true), s);
    }

    [Fact]
    public void A_repos_own_command_is_planted_unless_system_or_global_config_sets_the_same()
    {
        const string sys = "system\0filter.lfs.clean\ngit-lfs clean -- %f\0";
        Assert.Null(Parse.PlantedCommand(sys + "local\0filter.lfs.clean\ngit-lfs clean -- %f\0"));
        Assert.Equal("filter.lfs.clean", Parse.PlantedCommand(sys + "local\0filter.lfs.clean\nevil\0"));
        // A value whose first line matches the system one still differs as a whole.
        Assert.Equal("filter.lfs.clean", Parse.PlantedCommand(sys + "local\0filter.lfs.clean\ngit-lfs clean -- %f\nevil\0"));
        Assert.Equal("filter.x.process", Parse.PlantedCommand("worktree\0filter.x.process\nevil\0"));
        Assert.Equal("lfs.extension.e.clean", Parse.PlantedCommand("local\0lfs.extension.e.clean\nevil %f\0"));
        Assert.Equal("filter.x.clean", Parse.PlantedCommand("local\0filter.x.clean\0")); // a key with no value
        Assert.Null(Parse.PlantedCommand("global\0filter.x.clean\nmine\0command\0filter.y.clean\nx\0"));
        Assert.Null(Parse.PlantedCommand(""));
    }

    [Fact]
    public void Porcelain_detached_and_no_upstream()
    {
        var s = Parse.PorcelainV2("# branch.oid abc\r\n# branch.head (detached)\r\n");
        Assert.Equal("detached", s.Branch);
        Assert.False(s.HasUpstream);
        Assert.Equal(0, s.Changed);
    }

    [Theory]
    [InlineData("https://github.com/makers/orbit.git", "makers/orbit")]
    [InlineData("git@github.com:makers/orbit.git\n", "makers/orbit")]
    [InlineData("https://github.com/makers/orbit", "makers/orbit")]
    [InlineData("ssh://git@github.com/makers/my.repo.git", "makers/my.repo")]
    [InlineData("https://gitlab.com/makers/orbit.git", null)]
    [InlineData("https://github.com/makers/or\"bit", null)]
    public void GitHub_slug(string url, string? slug) => Assert.Equal(slug, Parse.GitHubSlug(url));

    [Fact]
    public void Handoff_skips_front_matter_headings_and_markdown()
    {
        var h = Parse.HandoffSnippet(
            "---\nname: x\n---\n# Handoff\n\n**SAFE POINT 2026-11-12**\n- Read [the plan](docs/plan.md) first\n```\ncode\n```\n| a | b |\n3. `build` is green\nfourth line\n")!;
        Assert.True(h.SafePoint);
        Assert.Equal("SAFE POINT 2026-11-12\nRead the plan first\nbuild is green", h.Snippet);
    }

    [Fact]
    public void Handoff_empty_note_is_null() => Assert.Null(Parse.HandoffSnippet("# Title\n\n## Sub\n"));

    [Theory]
    [InlineData(-5, "just now")]
    [InlineData(30, "just now")]
    [InlineData(600, "10 min ago")]
    [InlineData(7200, "2 h ago")]
    [InlineData(3 * 86400, "3 d ago")]
    [InlineData(90 * 86400, "3 mo ago")]
    public void Ago(int seconds, string text) => Assert.Equal(text, Parse.Ago(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Repo_node_from_graphql()
    {
        using var doc = JsonDocument.Parse("""
        {"visibility":"PUBLIC","pullRequests":{"totalCount":7,"nodes":[
          {"number":4,"title":"Fix","url":"https://github.com/a/b/pull/4","isDraft":false,
           "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"FAILURE"}}}]}},
          {"number":5,"title":"Draft","url":"https://github.com/a/b/pull/5","isDraft":true,
           "commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}]}}
        """);
        var r = Parse.Repo(doc.RootElement);
        Assert.Equal("PUBLIC", r.Visibility);
        Assert.Equal(7, r.PrTotal);
        Assert.Equal(["fail", "none"], r.Prs.Select(p => p.State));
        Assert.True(r.Prs[1].Draft);
    }

    [Fact]
    public void Actions_usage_counts_only_non_public_compute_against_the_allowance()
    {
        using var doc = JsonDocument.Parse("""
        {"usageItems":[
          {"date":"2026-11-02T10:00:00Z","product":"actions","sku":"Actions Linux","unitType":"Minutes","quantity":100,"grossAmount":0.6,"netAmount":0,"repositoryName":"secret"},
          {"date":"2026-11-02T11:00:00Z","product":"Actions","sku":"Actions Windows","unitType":"minutes","quantity":10,"grossAmount":0.1,"netAmount":0.8,"repositoryName":"gone"},
          {"date":"2026-11-03T09:00:00Z","product":"actions","sku":"Actions macOS 3-core","unitType":"Minutes","quantity":20.5,"grossAmount":1.271,"netAmount":0,"repositoryName":"Open"},
          {"date":"2026-11-05T09:00:00Z","product":"actions","sku":"Actions Linux","unitType":"Minutes","quantity":5,"grossAmount":0.03,"netAmount":0,"repositoryName":"open"},
          {"date":"2026-11-02T10:00:00Z","product":"actions","sku":"Actions storage","unitType":"GigabyteHours","quantity":3,"grossAmount":9,"netAmount":0.1,"repositoryName":"secret"},
          {"date":"2026-11-02T10:00:00Z","product":"git_lfs","unitType":"Minutes","quantity":999,"grossAmount":9,"netAmount":5}]}
        """);
        // "open" was public on the 3rd and private again from the 5th: each run counts by its own day.
        var log = new VisibilityLog(new Dictionary<string, string[]> { ["2026-11-01"] = [], ["2026-11-03"] = ["open"], ["2026-11-05"] = [] });
        var a = Parse.ActionsUsage(doc.RootElement, log.PublicOn);
        Assert.True(a.Known);
        Assert.Equal(135.5, a.Minutes);
        Assert.Equal(20.5, a.PublicMinutes);
        Assert.Equal(0.73, a.PrivateCost, 6); // "gone" is not listed as public, so it counts
        Assert.Equal(0.9, a.Net, 6);
        Assert.False(Parse.ActionsUsage(doc.RootElement, new VisibilityLog(new Dictionary<string, string[]> { ["2026-11-03"] = ["open"] }).PublicOn).Known);
        Assert.Equal(12, Parse.IncludedActions("free"));
        Assert.Null(Parse.IncludedActions("team"));
    }

    [Fact]
    public void Actions_chip_says_used_of_free_or_plainly_all_minutes()
    {
        var use = new Parse.ActionsUse(1343, 677, 6.47, 0, true);
        var b = Board.Build([], new Dictionary<string, RemoteInfo>(), new(true, true, "me", use, 12, false), false, DateTimeOffset.Now, false);
        Assert.Equal($"Actions ${6.47:0.00} of $12 free used", b.MinutesChip);
        Assert.Contains($"{666:N0} min in private", b.MinutesTip);
        Assert.Contains($"about {1078:N0} of {2000:N0} Linux minutes", b.MinutesTip);
        var plain = Board.Build([], new Dictionary<string, RemoteInfo>(), new(true, true, "me", use, null, false), false, DateTimeOffset.Now, false);
        Assert.Equal($"Actions {1343:N0} min this month", plain.MinutesChip);
        Assert.Contains("plan's free allowance", plain.MinutesTip);
        var learning = Board.Build([], new Dictionary<string, RemoteInfo>(), new(true, true, "me", use with { Known = false }, 12, false), false, DateTimeOffset.Now, false);
        Assert.Equal($"Actions {1343:N0} min this month", learning.MinutesChip);
        Assert.Contains("watched a whole month", learning.MinutesTip);
    }

    [Fact]
    public void Visibility_log_keeps_the_latest_reading_of_each_day_and_carries_it_forward()
    {
        var log = new VisibilityLog();
        Assert.Null(log.PublicOn("2026-11-14"));
        log.Record("2026-11-14", ["Fewturns"]);
        log.Record("2026-11-14", ["fewturns", "ironvolt"]); // a later reading the same day replaces it
        Assert.Null(log.PublicOn("2026-11-13"));
        Assert.Equal(["fewturns", "ironvolt"], log.PublicOn("2026-11-14")!.Order());
        Assert.True(log.PublicOn("2026-12-02")!.Contains("IronVolt")); // a day Laneboard didn't run keeps the day before's
        for (int d = 1; d <= 80; d++) log.Record(new DateTime(2027, 1, 1).AddDays(d).ToString("yyyy-MM-dd"), []);
        Assert.Null(log.PublicOn("2027-01-05")); // only the last 70 days are kept
    }
}

public class BoardTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    static LocalInfo L(string name, int changed = 0, double hours = 1, string? slug = null, bool upstream = true) =>
        new(@"C:\p\" + name, name, new("main", 1, 0, changed, upstream), Now.AddHours(-hours), "msg", slug, null, null, null);

    [Fact]
    public void Warnings_and_failing_checks_sort_first_then_recent()
    {
        var locals = new[] { L("old", hours: 50), L("recent", hours: 1), L("public", hours: 90, slug: "o/public"), L("failing", hours: 80, slug: "o/failing") };
        var remote = new Dictionary<string, RemoteInfo>
        {
            ["o/public"] = new("PUBLIC", [], 0, 1, 1),
            ["o/failing"] = new("PRIVATE", [new(1, "x", "https://github.com/o/failing/pull/1", "fail", false)], 3, 0, 0),
        };
        var b = Board.Build(locals, remote, new(true, true, "o", new(10, 0, 0.06, 1.5, true), 12, false), false, Now, false);

        Assert.Equal(["public", "failing", "recent", "old"], b.Projects.Select(p => p.Name));
        Assert.Contains("self-hosted runner", b.Projects[0].Warnings[0]);
        Assert.Equal("+2 more open", b.Projects[1].MorePrs);
        Assert.Equal("4 projects · 0 with changes · 3 open pull requests", b.Summary);
        Assert.Equal("Laneboard · 1 failing check", b.TrayText);
        Assert.Equal("Runners 1/1 online", b.RunnerChip);
        Assert.Contains(b.Warnings, w => w.Contains("$1.50"));
    }

    [Fact]
    public void Pinned_projects_come_after_needs_you_and_before_the_rest()
    {
        var locals = new[] { L("recent", hours: 1), L("pinned", hours: 90), L("failing", hours: 80, slug: "o/failing") };
        var remote = new Dictionary<string, RemoteInfo>
        {
            ["o/failing"] = new("PRIVATE", [new(1, "x", "https://github.com/o/failing/pull/1", "fail", false)], 1, 0, 0),
        };
        var b = Board.Build(locals, remote, null, false, Now, false, [@"c:\P\PINNED", @"C:\p\failing"]);

        Assert.Equal(["failing", "pinned", "recent"], b.Projects.Select(p => p.Name));
        Assert.Equal(["Needs you", "Pinned", "Other projects"], b.Projects.Select(p => p.Group));
        Assert.Equal("Unpin", b.Projects[1].PinLabel);
        Assert.Equal("Pin to top", b.Projects[2].PinLabel);
        Assert.Equal("recent, main, clean", b.Projects[2].SpokenName);
    }

    [Fact]
    public void Status_line_reads_like_a_sentence()
    {
        var b = Board.Build([L("a", changed: 3, hours: 2)], new Dictionary<string, RemoteInfo>(), null, false, Now, false);
        Assert.Equal("3 changed · 1 to push · last commit 2 h ago", b.Projects[0].StatusLine);
        var nb = Board.Build([L("b", upstream: false)], new Dictionary<string, RemoteInfo>(), null, false, Now, false);
        Assert.Equal("clean · branch not on a remote · last commit 1 h ago", nb.Projects[0].StatusLine);
    }

    [Fact]
    public void Missing_gh_is_said_once_and_plainly()
    {
        var b = Board.Build([], new Dictionary<string, RemoteInfo>(), new(false, false, null, null, null, false), false, Now, false);
        Assert.Single(b.Warnings);
        Assert.Contains("cli.github.com", b.Warnings[0]);
    }

    [Fact]
    public void Discover_finds_repos_two_levels_down_and_skips_hidden()
    {
        var root = Directory.CreateTempSubdirectory("laneboard-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "group", "b", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "group", "deep", "c", ".git")); // three levels: ignored
            Directory.CreateDirectory(Path.Combine(root, "node_modules", "d", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "hidden", ".git"));
            var found = Scanner.Discover([root], [Path.Combine(root, "hidden")]).Select(Path.GetFileName).Order().ToList();
            Assert.Equal(["a", "b"], found);
        }
        finally { Directory.Delete(root, true); }
    }
}
