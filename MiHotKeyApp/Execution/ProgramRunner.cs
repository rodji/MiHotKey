namespace MiHotKeyApp.Execution;

using System.Diagnostics;
using MiHotKeyApp.Config;
using Microsoft.Extensions.Logging;

internal sealed class ProgramRunner
{
    private readonly string _baseDir;
    private readonly ILogger _logger;

    public ProgramRunner(string baseDir, ILogger logger)
    {
        _baseDir = baseDir;
        _logger = logger;
    }

    public bool TryStart(string programId, ProgramConfig cfg, string? context)
    {
        try
        {
            var file = ExpandAndResolvePath(cfg.File);
            var args = Expand(cfg.Args);
            var workDir = string.IsNullOrWhiteSpace(cfg.WorkDir) ? "" : ExpandAndResolvePath(cfg.WorkDir);

            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = cfg.UseShellExecute,
            };

            if (!cfg.UseShellExecute)
            {
                if (cfg.Hidden)
                {
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                }

                if (cfg.CaptureOutput)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
                    psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
                }

                foreach (var (k, v) in cfg.Env)
                {
                    if (string.IsNullOrWhiteSpace(k))
                    {
                        continue;
                    }

                    psi.Environment[k] = Expand(v);
                }
            }
            else
            {
                if (cfg.Hidden)
                {
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                }
            }

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _logger.LogInformation(
                "program start id={id} shell={shell} hidden={hidden} file=\"{file}\" args=\"{args}\" wd=\"{wd}\" ctx=\"{ctx}\"",
                programId,
                cfg.UseShellExecute ? 1 : 0,
                cfg.Hidden ? 1 : 0,
                psi.FileName,
                psi.Arguments,
                psi.WorkingDirectory,
                context ?? "");

            if (!p.Start())
            {
                _logger.LogWarning("program start failed id={id} ctx=\"{ctx}\"", programId, context ?? "");
                return false;
            }

            var pid = p.Id;

            if (cfg.UseShellExecute || !cfg.CaptureOutput)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await p.WaitForExitAsync().ConfigureAwait(false);
                        _logger.LogInformation("program exit id={id} pid={pid} code={code} ctx=\"{ctx}\"", programId, pid, p.ExitCode, context ?? "");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "program wait failed id={id} pid={pid} ctx=\"{ctx}\"", programId, pid, context ?? "");
                    }
                    finally
                    {
                        p.Dispose();
                    }
                });

                return true;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var stdoutTask = ReadToEndBoundedAsync(p.StandardOutput, maxChars: 8192);
                    var stderrTask = ReadToEndBoundedAsync(p.StandardError, maxChars: 8192);

                    await p.WaitForExitAsync().ConfigureAwait(false);
                    var stdout = await stdoutTask.ConfigureAwait(false);
                    var stderr = await stderrTask.ConfigureAwait(false);

                    _logger.LogInformation("program exit id={id} pid={pid} code={code} ctx=\"{ctx}\"", programId, pid, p.ExitCode, context ?? "");
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        _logger.LogInformation("program stdout id={id} pid={pid} text=\"{text}\"", programId, pid, stdout);
                    }

                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        _logger.LogWarning("program stderr id={id} pid={pid} text=\"{text}\"", programId, pid, stderr);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "program monitor failed id={id} pid={pid} ctx=\"{ctx}\"", programId, pid, context ?? "");
                }
                finally
                {
                    p.Dispose();
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "program start exception id={id} ctx=\"{ctx}\"", programId, context ?? "");
            return false;
        }
    }

    private string Expand(string text) => Environment.ExpandEnvironmentVariables(text ?? "");

    private string ExpandAndResolvePath(string path)
    {
        var expanded = Expand(path);
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return "";
        }

        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        if (expanded.StartsWith(".") || expanded.Contains('\\') || expanded.Contains('/'))
        {
            return Path.GetFullPath(Path.Combine(_baseDir, expanded));
        }

        return expanded;
    }

    private static async Task<string> ReadToEndBoundedAsync(StreamReader reader, int maxChars)
    {
        var buffer = new char[1024];
        var sb = new System.Text.StringBuilder(Math.Min(maxChars, 1024));

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            AppendBounded(sb, buffer.AsSpan(0, read), maxChars);
        }

        return sb.ToString().Trim();
    }

    private static void AppendBounded(System.Text.StringBuilder sb, ReadOnlySpan<char> chunk, int maxChars)
    {
        if (chunk.Length >= maxChars)
        {
            sb.Clear();
            sb.Append(chunk[^maxChars..]);
            return;
        }

        var overflow = (sb.Length + chunk.Length) - maxChars;
        if (overflow > 0)
        {
            sb.Remove(0, overflow);
        }

        sb.Append(chunk);
    }
}

