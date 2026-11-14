namespace Laneboard;

/// Made-up projects for screenshots and the Store listing (`--demo`): no real names ever appear.
static class Demo
{
    public static (List<LocalInfo>, Dictionary<string, RemoteInfo>, AccountInfo) Data(DateTimeOffset now)
    {
        LocalInfo L(string name, string branch, int changed, int ahead, int behind, double hours, string subject,
            string? handoff = null, bool safe = false) =>
            new(@"C:\dev\" + name, name, new Parse.GitStatus(branch, ahead, behind, changed, true),
                now.AddHours(-hours), subject, "studio/" + name,
                handoff == null ? null : new Parse.Handoff(handoff, safe),
                handoff == null ? null : @"C:\dev\" + name + @"\HANDOFF.md",
                handoff == null ? null : now.AddHours(-hours / 3));

        var locals = new List<LocalInfo>
        {
            L("orbit-notes", "feat/offline-sync", 4, 2, 0, 0.6, "Merge offline edits on reconnect",
                "Offline edits now merge on reconnect; the conflict view is next.\nTests green on Windows and macOS.", true),
            L("pixel-harbor", "feat/level-editor", 12, 0, 0, 3, "Level editor: save to JSON",
                "Level editor saves to JSON. Undo stack is half done: see editor/undo.ts."),
            L("kiln-cli", "fix/windows-paths", 1, 1, 0, 0.3, "Normalise drive letters"),
            L("tidewatch-api", "main", 0, 0, 1, 26, "Rate limits per API key"),
            L("studio-site", "main", 0, 0, 0, 72, "New pricing page"),
        };
        PrView P(int n, string t, string s, bool d = false) => new(n, t, $"https://github.com/studio/x/pull/{n}", s, d);
        var remote = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["studio/orbit-notes"] = new("PRIVATE", [P(42, "Offline sync: merge edits on reconnect", "pending"), P(39, "Docs: conflict view", "pass")], 2, 0, 0),
            ["studio/pixel-harbor"] = new("PRIVATE", [P(18, "Level editor", "fail")], 1, 0, 0),
            ["studio/kiln-cli"] = new("PUBLIC", [P(7, "Windows paths with drive letters", "pass")], 1, 1, 1),
            ["studio/tidewatch-api"] = new("PRIVATE", [P(31, "Per-key rate limits", "pass", true)], 1, 0, 0),
            ["studio/studio-site"] = new("PUBLIC", [], 0, 0, 0),
        };
        return (locals, remote, new AccountInfo(true, true, "studio", new(412, 160, 2.35, 0), 12, false));
    }
}
