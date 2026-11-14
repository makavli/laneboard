namespace Laneboard;

public sealed record LocalInfo(
    string Path, string Name, Parse.GitStatus Git,
    DateTimeOffset? LastCommit, string? LastSubject, string? Slug,
    Parse.Handoff? Handoff, string? HandoffPath, DateTimeOffset? HandoffTime,
    string? Error = null);

public sealed record RemoteInfo(string Visibility, IReadOnlyList<PrView> Prs, int PrTotal, int RunnersTotal, int RunnersOnline);

public sealed record AccountInfo(bool GhFound, bool SignedIn, string? Login, double? Minutes, double? Net, bool NeedsUserScope);

public sealed record PrView(int Number, string Title, string Url, string State, bool Draft)
{
    public string Label => $"#{Number}  {Title}";
    public string Tip => $"{Title}\nChecks: {State switch { "pass" => "passing", "fail" => "failing", "pending" => "running", _ => "none" }}{(Draft ? " · draft" : "")}";
}

public sealed record ProjectView(
    string Name, string Path, string Branch, bool IsPublic, string StatusLine, string? StatusTip,
    IReadOnlyList<string> Warnings, IReadOnlyList<PrView> Prs, string? MorePrs,
    string? Handoff, string? HandoffMeta, bool SafePoint, string? HandoffPath,
    string? Slug, DateTimeOffset LastTouched, int Changed, bool CanCode)
{
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
