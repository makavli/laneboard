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
    public void Actions_usage_sums_minutes_and_net_amount()
    {
        using var doc = JsonDocument.Parse("""
        {"usageItems":[
          {"product":"actions","unitType":"Minutes","quantity":120.5,"netAmount":0},
          {"product":"Actions","unitType":"minutes","quantity":10,"netAmount":0.8},
          {"product":"actions","unitType":"GigabyteHours","quantity":3,"netAmount":0.1},
          {"product":"packages","unitType":"Minutes","quantity":999,"netAmount":5}]}
        """);
        var (min, net) = Parse.ActionsUsage(doc.RootElement);
        Assert.Equal(130.5, min);
        Assert.Equal(0.9, net, 6);
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
        var b = Board.Build(locals, remote, new(true, true, "o", 10, 1.5, false), false, Now, false);

        Assert.Equal(["public", "failing", "recent", "old"], b.Projects.Select(p => p.Name));
        Assert.Contains("self-hosted runner", b.Projects[0].Warnings[0]);
        Assert.Equal("+2 more open", b.Projects[1].MorePrs);
        Assert.Equal("4 projects · 0 with changes · 3 open pull requests", b.Summary);
        Assert.Equal("Laneboard · 1 failing check", b.TrayText);
        Assert.Equal("Runners 1/1 online", b.RunnerChip);
        Assert.Contains(b.Warnings, w => w.Contains("$1.50"));
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
