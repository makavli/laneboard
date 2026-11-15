using System.IO;
using Laneboard;
using Xunit;

public class ShellTests
{
    [Fact]
    public void A_semicolon_in_a_folder_name_never_starts_a_second_terminal_command()
    {
        var wt = Path.GetTempFileName(); // stands in for wt.exe: only its existence matters
        try
        {
            Assert.Equal(new[] { "-d", @"C:\x\a\;new-tab cmd" }, Shell.TerminalStart(@"C:\x\a;new-tab cmd", wt).ArgumentList);
            Assert.Equal(new[] { "-d", @"C:\x\\;y" }, Shell.TerminalStart(@"C:\x\;y", wt).ArgumentList);
        }
        finally { File.Delete(wt); }
    }

    [Fact]
    public void Without_windows_terminal_powershell_comes_from_system32_never_the_folder()
    {
        var psi = Shell.TerminalStart(@"C:\x\repo", Path.Combine(Path.GetTempPath(), "no-wt-" + Guid.NewGuid().ToString("N"), "wt.exe"));
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.Equal(@"C:\x\repo", psi.WorkingDirectory);
    }
}
