using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Laneboard;

/// Everything from GitHub goes through the user's own GitHub CLI (`gh`): no token is stored by this app.
public static class GitHub
{
    static readonly Regex Safe = new("^[A-Za-z0-9._-]+$");

    // One GraphQL query covers up to 25 repositories: visibility + the 5 most recent open PRs and their checks.
    const string RepoQuery =
        "ALIAS:repository(owner:\"OWNER\",name:\"NAME\"){visibility pullRequests(states:OPEN,first:5,orderBy:{field:UPDATED_AT,direction:DESC})" +
        "{totalCount nodes{number title url isDraft commits(last:1){nodes{commit{statusCheckRollup{state}}}}}}}";

    public static async Task<Dictionary<string, RemoteInfo>> Repos(IReadOnlyList<string> slugs)
    {
        var map = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase);
        // Names come from local git config: only plain GitHub names may enter the query.
        var ok = slugs.Where(s => s.Split('/') is [var o, var n] && Safe.IsMatch(o) && Safe.IsMatch(n)).ToList();

        foreach (var chunk in ok.Chunk(25))
        {
            var q = new StringBuilder("query{");
            for (int i = 0; i < chunk.Length; i++)
            {
                var p = chunk[i].Split('/');
                q.Append(RepoQuery.Replace("ALIAS", $"r{i}").Replace("OWNER", p[0]).Replace("NAME", p[1])).Append(' ');
            }
            q.Append('}');
            var r = await Proc.Run("gh", null, ["api", "graphql", "-f", "query=" + q]);
            if (!r.Out.TrimStart().StartsWith('{')) continue; // gh missing or signed out: Account() reports it
            try
            {
                using var doc = JsonDocument.Parse(r.Out);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                for (int i = 0; i < chunk.Length; i++)
                    if (data.TryGetProperty($"r{i}", out var repo) && repo.ValueKind == JsonValueKind.Object)
                        map[chunk[i]] = Parse.Repo(repo);
            }
            catch (JsonException) { /* partial output: keep what we have */ }
        }

        // Self-hosted runners: REST only, and only answers for repos where the user is an admin.
        var runners = new ConcurrentDictionary<string, (int, int)>();
        await Parallel.ForEachAsync(map.Keys.ToList(), new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (slug, _) =>
        {
            var r = await Proc.Run("gh", null, ["api", $"repos/{slug}/actions/runners", "-q", "[.runners[].status] | join(\",\")"]);
            if (r.Code != 0) return;
            var states = r.Out.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (states.Length > 0) runners[slug] = (states.Length, states.Count(s => s == "online"));
        });
        foreach (var (slug, (total, online)) in runners)
            map[slug] = map[slug] with { RunnersTotal = total, RunnersOnline = online };
        return map;
    }

    public static async Task<AccountInfo> Account()
    {
        var u = await Proc.Run("gh", null, ["api", "user", "-q", ".login"]);
        if (u.Code == -1) return new(false, false, null, null, null, false);
        var login = u.Out.Trim();
        if (u.Code != 0 || !Safe.IsMatch(login)) return new(true, false, null, null, null, false);

        var now = DateTime.Now;
        // shortcut: personal accounts on GitHub's billing platform only; organisation billing is not read.
        var b = await Proc.Run("gh", null, ["api", $"users/{login}/settings/billing/usage?year={now.Year}&month={now.Month}"]);
        if (b.Code == 0 && b.Out.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(b.Out);
                var (min, net) = Parse.ActionsUsage(doc.RootElement);
                return new(true, true, login, min, net, false);
            }
            catch (JsonException) { }
        }
        bool needsScope = (b.Out + b.Err).Contains("\"user\" scope");
        return new(true, true, login, null, null, needsScope);
    }
}
