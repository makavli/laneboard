namespace Laneboard;

/// One Windows notification. Target: a web address or a file to open on click; null opens the flyout.
public sealed record Alert(string Title, string Body, string? Target);

/// What changed between two boards that is worth a notification. Pure, so it is unit-tested.
public static class Alerts
{
    /// Only changes to what both boards show: a project or pull request seen for the first time says nothing,
    /// and a GitHub read that failed (no pull requests, no runners) never reads as "runner offline".
    public static List<Alert> Diff(Board before, Board after)
    {
        var old = before.Projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var list = new List<Alert>();
        foreach (var p in after.Projects)
        {
            if (!old.TryGetValue(p.Path, out var b)) continue;
            foreach (var pr in p.Prs)
            {
                var was = b.Prs.FirstOrDefault(x => x.Number == pr.Number)?.State;
                if (was == null || was == pr.State) continue;
                if (pr.State == "fail") list.Add(new("Checks failing", $"{p.Name} · #{pr.Number} {pr.Title}", pr.Url));
                else if (pr.State == "pass") list.Add(new("Checks passed", $"{p.Name} · #{pr.Number} {pr.Title}", pr.Url));
            }
            if (p.SafePoint && (!b.SafePoint || b.Handoff != p.Handoff))
            {
                var line = p.Handoff?.Split('\n')[0];
                list.Add(new("Safe point", string.IsNullOrEmpty(line) ? p.Name : $"{p.Name} · {line}", p.HandoffPath));
            }
            if (p.RunnersTotal > 0 && p.RunnersOnline < b.RunnersOnline)
                list.Add(new("Runner offline", $"{p.Name} · {p.RunnersOnline} of {p.RunnersTotal} runners online",
                    p.Slug == null ? null : $"https://github.com/{p.Slug}/settings/actions/runners"));
        }
        return list;
    }

    /// Several changes at once become one notification of two lines (a toast shows three, title included);
    /// its click opens the flyout.
    public static Alert? Combine(IReadOnlyList<Alert> list)
    {
        if (list.Count <= 1) return list.FirstOrDefault();
        string Line(Alert a) => $"{a.Title}: {a.Body}";
        var second = list.Count == 2 ? Line(list[1]) : $"and {list.Count - 1} more";
        return new($"{list.Count} updates", Line(list[0]) + "\n" + second, null);
    }
}
