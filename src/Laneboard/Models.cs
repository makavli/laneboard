namespace Laneboard;

public sealed record LocalInfo(
    string Path, string Name, Parse.GitStatus Git,
    DateTimeOffset? LastCommit, string? LastSubject, string? Slug,
    Parse.Handoff? Handoff, string? HandoffPath, DateTimeOffset? HandoffTime,
    string? Error = null);

public sealed record RemoteInfo(string Visibility, IReadOnlyList<PrView> Prs, int PrTotal, int RunnersTotal, int RunnersOnline);

/// Included = the plan's monthly Actions allowance in dollars, null when the plan or the repos' visibility is unknown.
public sealed record AccountInfo(bool GhFound, bool SignedIn, string? Login, Parse.ActionsUse? Actions, double? Included, bool NeedsUserScope);

public sealed record PrView(int Number, string Title, string Url, string State, bool Draft)
{
    public string Label => $"#{Number}  {Title}";
    public string Tip => $"{Title}\nChecks: {State switch { "pass" => "passing", "fail" => "failing", "pending" => "running", _ => "none" }}{(Draft ? " · draft" : "")}";
}

public sealed record ProjectView(
    string Name, string Path, string Branch, bool IsPublic, string StatusLine, string? StatusTip,
    IReadOnlyList<string> Warnings, IReadOnlyList<PrView> Prs, string? MorePrs,
    string? Handoff, string? HandoffMeta, bool SafePoint, string? HandoffPath,
    string? Slug, DateTimeOffset LastTouched, int Changed, bool CanCode, int RunnersOnline = 0, int RunnersTotal = 0,
    bool Pinned = false)
{
    /// Something is wrong: it puts the badge on the tray icon.
    public bool Problem => HasWarnings || HasFailing;
    /// Waits for the user: a problem, or a session stopped at a safe point. Listed first, under "Needs you".
    public bool NeedsYou => Problem || SafePoint;
    /// "Needs you" wins over "Pinned": a pinned project with a problem moves up to where problems are.
    public string Group => NeedsYou ? "Needs you" : Pinned ? "Pinned" : "Other projects";
    public string PinLabel => Pinned ? "Unpin" : "Pin to top";

    /// What a screen reader says for the project, since the coloured dot alone carries the check state.
    public string SpokenName =>
        $"{Name}, {Branch}, {LightLine}" +
        (WorstState switch { "pass" => ", checks passing", "pending" => ", checks running", _ => "" }) +
        (HasWarnings ? ", warning: " + string.Join(" ", Warnings) : "");
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasPrs => Prs.Count > 0;
    public bool HasMorePrs => MorePrs != null;
    public bool HasHandoff => HandoffPath != null;
    public bool HasSlug => Slug != null;
    public bool HasFailing => Prs.Any(p => p.State == "fail");
    public string WarningTip => string.Join("\n", Warnings);

    /// The worst check state among the open pull requests: fail > pending > pass > none.
    public string WorstState =>
        HasFailing ? "fail" : Prs.Any(p => p.State == "pending") ? "pending" : Prs.Any(p => p.State == "pass") ? "pass" : "none";

    /// The light view's one line: what changed, what needs a look.
    public string LightLine
    {
        get
        {
            var parts = new List<string> { Changed == 0 ? "clean" : $"{Changed} changed" };
            int open = Prs.Count + (MorePrs is { } m && int.TryParse(m.Split(' ')[0].TrimStart('+'), out var x) ? x : 0);
            if (Prs.FirstOrDefault(p => p.State == "fail") is { } f) parts.Add($"checks failing on #{f.Number}");
            else if (open > 0) parts.Add($"{open} open PR{(open == 1 ? "" : "s")}");
            if (SafePoint) parts.Add("safe point");
            return string.Join(" · ", parts);
        }
    }
}
