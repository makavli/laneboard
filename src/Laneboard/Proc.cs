using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Laneboard;

public static class Proc
{
    public sealed record Result(int Code, string Out, string Err);

    /// Runs a console tool with no window ever shown. Code -1 = tool not found, -2 = timed out.
    public static async Task<Result> Run(string exe, string? cwd, IEnumerable<string> args, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Never let git or gh stop to ask a question nobody can see.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { /* already gone */ }
                return new(-2, "", "timed out");
            }
            return new(p.ExitCode, await o, await e);
        }
        catch (Win32Exception ex) { return new(-1, "", ex.Message); }
    }
}
