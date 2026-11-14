using Laneboard;
using Xunit;

public class AlertsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    static LocalInfo L(string name, double hours = 1, string? handoff = null, bool safe = false) =>
        new(@"C:\p\" + name, name, new("main", 0, 0, 0, true), Now.AddHours(-hours), "msg", "o/" + name,
            handoff == null ? null : new Parse.Handoff(handoff, safe), handoff == null ? null : @"C:\p\" + name + @"\HANDOFF.md", Now);

    static PrView P(int n, string state) => new(n, "Title", $"https://github.com/o/a/pull/{n}", state, false);

    static RemoteInfo R(int online = 0, int total = 0, params PrView[] prs) => new("PRIVATE", prs, prs.Length, total, online);

    static Board B(Dictionary<string, RemoteInfo> remote, params LocalInfo[] locals) =>
        Board.Build(locals, remote, null, false, Now, false);

    [Fact]
    public void First_sight_and_unchanged_say_nothing()
    {
        var before = B(new() { ["o/a"] = R(prs: [P(1, "pending")]) }, L("a"));
        var after = B(new() { ["o/a"] = R(prs: [P(1, "pending"), P(2, "fail")]) }, L("a"), L("b", handoff: "Done", safe: true));
        Assert.Empty(Alerts.Diff(before, after));
    }

    [Fact]
    public void Checks_that_fail_or_pass_are_announced()
    {
        var before = B(new() { ["o/a"] = R(prs: [P(1, "pending"), P(2, "pass"), P(3, "fail")]) }, L("a"));
        var after = B(new() { ["o/a"] = R(prs: [P(1, "pass"), P(2, "fail"), P(3, "pending")]) }, L("a"));
        Assert.Equal(
            [new Alert("Checks passed", "a · #1 Title", "https://github.com/o/a/pull/1"),
             new Alert("Checks failing", "a · #2 Title", "https://github.com/o/a/pull/2")],
            Alerts.Diff(before, after));
    }

    [Fact]
    public void A_new_safe_point_is_announced_once()
    {
        var none = new Dictionary<string, RemoteInfo>();
        var working = B(none, L("a", handoff: "Working on it"));
        var safe = B(none, L("a", handoff: "Done\nmore", safe: true));
        Assert.Equal([new Alert("Safe point", "a · Done", @"C:\p\a\HANDOFF.md")], Alerts.Diff(working, safe));
        Assert.Empty(Alerts.Diff(safe, B(none, L("a", handoff: "Done\nmore", safe: true))));
        Assert.Single(Alerts.Diff(safe, B(none, L("a", handoff: "Next one done", safe: true))));
    }

    [Fact]
    public void Runner_offline_but_not_when_GitHub_was_unreachable()
    {
        var before = B(new() { ["o/a"] = R(1, 1) }, L("a"));
        var offline = B(new() { ["o/a"] = R(0, 1) }, L("a"));
        Assert.Equal([new Alert("Runner offline", "a · 0 of 1 runners online", "https://github.com/o/a/settings/actions/runners")],
            Alerts.Diff(before, offline));
        Assert.Empty(Alerts.Diff(before, B(new(), L("a")))); // gh failed: no runners known, nothing said
    }

    [Fact]
    public void Several_alerts_become_one()
    {
        Assert.Null(Alerts.Combine([]));
        var one = new Alert("Safe point", "a", null);
        Assert.Same(one, Alerts.Combine([one]));
        var five = Enumerable.Range(1, 5).Select(i => new Alert("Checks passed", $"p{i}", $"https://x/{i}")).ToList();
        var c = Alerts.Combine(five)!;
        Assert.Equal("5 updates", c.Title);
        Assert.Equal("Checks passed: p1\nand 4 more", c.Body);
        Assert.Null(c.Target);
        Assert.Equal("Checks passed: p1\nChecks passed: p2", Alerts.Combine(five.Take(2).ToList())!.Body);
    }

    [Fact]
    public void Problems_then_safe_points_come_first_and_only_problems_badge_the_tray()
    {
        var b = B(new() { ["o/failing"] = R(prs: [P(1, "fail")]) },
            L("plain", hours: 1), L("safe", hours: 5, handoff: "Done", safe: true), L("failing", hours: 9));
        Assert.Equal(["failing", "safe", "plain"], b.Projects.Select(p => p.Name));
        Assert.Equal(["Needs you", "Needs you", "Other projects"], b.Projects.Select(p => p.Group));
        Assert.True(b.Badge);
        Assert.False(B(new(), L("safe", handoff: "Done", safe: true)).Badge);
    }
}

public class TrayTests
{
    [Fact]
    public void Badge_is_a_red_dot_in_the_lower_right_corner()
    {
        using var blue = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(blue)) g.Clear(System.Drawing.Color.Blue);
        using var plain = System.Drawing.Icon.FromHandle(blue.GetHicon());
        using var badged = Tray.WithDot(plain);
        using var px = badged.ToBitmap();
        Assert.Equal(System.Drawing.Color.FromArgb(255, 0xC4, 0x2B, 0x1C), px.GetPixel(24, 24)); // the dot
        Assert.Equal(System.Drawing.Color.Blue.ToArgb(), px.GetPixel(4, 4).ToArgb());            // the icon, untouched
        Assert.Equal(0, px.GetPixel(15, 24).A);                                                   // the cut-out ring
    }
}
