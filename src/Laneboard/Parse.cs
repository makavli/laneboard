using System.Text.Json;
using System.Text.RegularExpressions;

namespace Laneboard;

/// Pure parsing of what git and the GitHub CLI print. No I/O here, so all of it is unit-tested.
public static class Parse
{
    public sealed record GitStatus(string Branch, int Ahead, int Behind, int Changed, bool HasUpstream);

    public sealed record Handoff(string Snippet, bool SafePoint);

    /// `git status --porcelain=v2 --branch`
    public static GitStatus PorcelainV2(string text)
    {
        string branch = "detached";
        int ahead = 0, behind = 0, changed = 0;
        bool upstream = false;
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("# branch.head "))
            {
                var h = l["# branch.head ".Length..];
                branch = h == "(detached)" ? "detached" : h;
            }
            else if (l.StartsWith("# branch.upstream ")) upstream = true;
            else if (l.StartsWith("# branch.ab "))
            {
                var p = l["# branch.ab ".Length..].Split(' ');
                if (p.Length == 2)
                {
                    int.TryParse(p[0].TrimStart('+'), out ahead);
                    int.TryParse(p[1].TrimStart('-'), out behind);
                }
            }
            else if (l.Length > 0 && l[0] != '#') changed++;
        }
        return new(branch, ahead, behind, changed, upstream);
    }

    static readonly Regex GitHubRemote = new(@"github\.com[:/]+([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// "owner/repo" for a github.com remote, else null.
    public static string? GitHubSlug(string url)
    {
        var m = GitHubRemote.Match(url.Trim());
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }

    /// `git config --show-scope -z` prints "scope\0key\nvalue\0" per entry. The first key the repo itself sets (local or
    /// worktree scope) that the system or global config doesn't set to the same value, else null: Git for Windows'
    /// system config already holds the LFS filter, and clones that repeat it locally stay readable.
    public static string? PlantedCommand(string z)
    {
        var p = z.Split('\0');
        var entries = Enumerable.Range(0, p.Length / 2).Select(i => (Scope: p[2 * i], Entry: p[2 * i + 1])).ToList();
        var trusted = entries.Where(e => e.Scope is "system" or "global").Select(e => e.Entry).ToHashSet();
        return entries.Where(e => e.Scope is not ("system" or "global" or "command") && !trusted.Contains(e.Entry))
            .Select(e => e.Entry.Split('\n')[0]).FirstOrDefault();
    }

    static readonly Regex ListMarker = new(@"^([-*+]|\d+[.)])\s+");
    static readonly Regex MdLink = new(@"\[([^\]]+)\]\([^)]*\)");

    /// The first lines of a handoff note worth reading, with markdown noise removed.
    public static Handoff? HandoffSnippet(string text, int maxLines = 3)
    {
        var lines = text.Replace("\r", "").Split('\n');
        bool safe = lines.Take(30).Any(l => l.Contains("SAFE POINT", StringComparison.OrdinalIgnoreCase));
        int i = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---") // YAML front matter
        {
            i = 1;
            while (i < lines.Length && lines[i].Trim() != "---") i++;
            i++;
        }
        var picked = new List<string>();
        bool inFence = false;
        for (; i < lines.Length && picked.Count < maxLines; i++)
        {
            var l = lines[i].Trim();
            if (l.StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence || l.Length == 0 || l[0] is '#' or '|' or '>' || l.StartsWith("---") || l.StartsWith("<!--")) continue;
            l = ListMarker.Replace(l, "");
            l = MdLink.Replace(l, "$1").Replace("**", "").Replace("__", "").Replace("`", "");
            if (l.Length == 0) continue;
            if (l.Length > 160) l = l[..157].TrimEnd() + "…";
            picked.Add(l);
        }
        return picked.Count == 0 && !safe ? null : new(string.Join("\n", picked), safe);
    }

    public static string Ago(TimeSpan t) =>
        t.TotalMinutes < 1 ? "just now" :
        t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago" :
        t.TotalDays < 1 ? $"{(int)t.TotalHours} h ago" :
        t.TotalDays < 30 ? $"{(int)t.TotalDays} d ago" :
        t.TotalDays < 365 ? $"{(int)(t.TotalDays / 30)} mo ago" :
        $"{(int)(t.TotalDays / 365)} y ago";

    /// GitHub's statusCheckRollup.state -> pass | fail | pending | none
    public static string CheckState(string? rollup) => rollup switch
    {
        "SUCCESS" => "pass",
        "FAILURE" or "ERROR" => "fail",
        "PENDING" or "EXPECTED" => "pending",
        _ => "none",
    };

    /// One `repository { ... }` node of the GraphQL answer.
    public static RemoteInfo Repo(JsonElement repo)
    {
        var prs = new List<PrView>();
        int total = 0;
        if (repo.TryGetProperty("pullRequests", out var pr) && pr.ValueKind == JsonValueKind.Object)
        {
            total = pr.TryGetProperty("totalCount", out var tc) && tc.TryGetInt32(out var n) ? n : 0;
            if (pr.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                foreach (var node in nodes.EnumerateArray())
                {
                    string? state = null;
                    if (node.TryGetProperty("commits", out var c) && c.TryGetProperty("nodes", out var cn)
                        && cn.ValueKind == JsonValueKind.Array && cn.GetArrayLength() > 0
                        && cn[0].TryGetProperty("commit", out var commit)
                        && commit.TryGetProperty("statusCheckRollup", out var roll) && roll.ValueKind == JsonValueKind.Object)
                        state = Str(roll, "state");
                    prs.Add(new PrView(
                        node.TryGetProperty("number", out var num) && num.TryGetInt32(out var nn) ? nn : 0,
                        Str(node, "title"), Str(node, "url"),
                        CheckState(state),
                        node.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True));
                }
        }
        return new RemoteInfo(Str(repo, "visibility"), prs, total, 0, 0);
    }

    /// Minutes = all minutes run; PrivateCost = what the non-public ones cost at each runner's rate, which is what the
    /// plan's dollar allowance pays for (public repos on standard runners are free); Net = what is actually billed.
    /// Known = the visibility of every run's repository on its day was known, so PublicMinutes and PrivateCost hold.
    public sealed record ActionsUse(double Minutes, double PublicMinutes, double PrivateCost, double Net, bool Known);

    /// GitHub's billing usage report for one month. publicOn(day) = the repos public that day (null: not known);
    /// a repo not in it counts as private.
    public static ActionsUse ActionsUsage(JsonElement root, Func<string, IReadOnlySet<string>?> publicOn)
    {
        double minutes = 0, pub = 0, cost = 0, net = 0;
        bool known = true;
        if (root.TryGetProperty("usageItems", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var it in items.EnumerateArray())
            {
                if (!Str(it, "product").Equals("actions", StringComparison.OrdinalIgnoreCase)) continue;
                net += Num(it, "netAmount");
                if (!Str(it, "unitType").Equals("minutes", StringComparison.OrdinalIgnoreCase)) continue; // storage has its own allowance
                double q = Num(it, "quantity");
                minutes += q;
                var date = Str(it, "date");
                if ((date.Length >= 10 ? publicOn(date[..10]) : null) is not { } publicThatDay) known = false;
                else if (publicThatDay.Contains(Str(it, "repositoryName"))) pub += q;
                else cost += Num(it, "grossAmount");
            }
        return new(minutes, pub, cost, net, known);
    }

    /// Monthly included Actions compute in dollars by plan (docs.github.com, billing for GitHub Actions).
    public static double? IncludedActions(string plan) => plan.ToLowerInvariant() switch
    {
        "free" => 12, "pro" => 18, _ => null,
    };

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
