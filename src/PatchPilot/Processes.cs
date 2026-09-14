using System.Diagnostics;
using System.Text;

namespace PatchPilot;

/// <summary>Argument-list execution without shell evaluation; child processes receive no agent secrets.</summary>
public static class Processes
{
    public static async Task<string> Run(string executable, IEnumerable<string> args, string root,
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        // Trusted host PATH only; never include the target repository or a writable tools directory.
        foreach (var name in new[] { "PATH", "HOME", "SystemRoot", "TEMP", "TMP", "DOCKER_HOST" })
            if (Environment.GetEnvironmentVariable(name) is { } value) info.Environment[name] = value;
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start process.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        static async Task<string> Drain(StreamReader reader, CancellationToken token)
        {
            var result = new StringBuilder();
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
            {
                if (result.Length + read > 2_000_000) throw new InvalidDataException("Process output limit exceeded.");
                result.Append(buffer, 0, read);
            }
            return result.ToString();
        }
        try
        {
            var output = Drain(process.StandardOutput, timeout.Token);
            var errors = Drain(process.StandardError, timeout.Token);
            // Reading streams concurrently prevents stdout/stderr pipe deadlock.
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Command failed with exit code " + process.ExitCode +
                    ". Raw output intentionally withheld; reproduce on an isolated runner.");
            return await output;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
    public static Task<string> Git(string root, CancellationToken ct, params string[] args) =>
        Run("git", new[] { "-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false",
            "-c", "core.untrackedCache=false" }.Concat(args), root, 60, ct);

    public static async Task<CheckResult[]> Checks(Profile profile, string root, CancellationToken ct)
    {
        if (profile.Checks.Length == 0) throw new InvalidDataException("No validation checks configured.");
        var results = new List<CheckResult>();
        foreach (var command in profile.Checks)
        {
            var args = new List<string> { "run", "--rm", "--init", "--cap-drop=ALL",
                "--security-opt=no-new-privileges", "--read-only", "--pids-limit=256",
                "--memory=4g", "--cpus=2", "--network", command.Network,
                "--tmpfs", "/tmp:rw,nosuid,size=1g", "--env", "HOME=/tmp",
                "--user", "1000:1000", "--workdir", "/work",
                "--mount", "type=bind,src=" + Path.GetFullPath(root) + ",dst=/work",
                "--mount", "type=bind,src=" + Path.Combine(Path.GetFullPath(root), ".git") + ",dst=/work/.git,readonly" };
            if (root.Contains(',')) throw new InvalidDataException("Workspace path cannot contain commas.");
            args.Add(command.Image);
            args.AddRange(command.Args);
            await Run("docker", args, root, command.TimeoutSeconds, ct);
            results.Add(new(command.Image, command.Args, 0));
        }
        return results.ToArray();
    }
}
