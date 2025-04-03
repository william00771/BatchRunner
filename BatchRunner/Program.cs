using System.Diagnostics;
using BatchRunner.Models;
using BatchRunner.Services;
using BatchRunner.Util;

class Program
{
    static List<Process> RunningProcesses = new();

    static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Logger.Log("CTRL+C received. Terminating all child processes...");
            lock (RunningProcesses)
            {
                foreach (var p in RunningProcesses)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(true);
                            Logger.Log($"Killed {Path.GetFileName(p.StartInfo.FileName)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error killing process: {ex.Message}");
                    }
                }
            }
            Environment.Exit(0);
        };

        var apps = ParseArguments(args);
        foreach (var app in apps)
        {
            _ = Task.Run(() => RunAppLoop(app));
        }

        Logger.Log("All apps started.");
        await Task.Delay(-1);
    }

    static List<AppTask> ParseArguments(string[] args)
    {
        var list = new List<AppTask>();
        int i = 0;

        while (i < args.Length)
        {
            if (!args[i].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            string path = args[i++];
            var argList = new List<string>();
            int? interval = null;
            string? currentConn = null;
            var steps = new List<SqlStep>();

            while (i < args.Length && !args[i].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string current = args[i];

                if (current.StartsWith("-Interval", StringComparison.OrdinalIgnoreCase))
                {
                    string valStr = current.Equals("-Interval", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                        ? args[i + 1]
                        : current.Substring("-Interval".Length);

                    if (int.TryParse(valStr, out int val))
                    {
                        interval = val;
                        i += current.Equals("-Interval", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                        continue;
                    }
                }

                if (current == "-ConnectionString" && i + 1 < args.Length)
                {
                    currentConn = args[++i];
                    i++;
                    continue;
                }

                if (current == "-RunSqlCommand" && i + 1 < args.Length)
                {
                    if (string.IsNullOrWhiteSpace(currentConn))
                        throw new Exception("You must specify -ConnectionString before -RunSqlCommand.");

                    steps.Add(new RunSqlCommand(args[++i], currentConn));
                    i++;
                    continue;
                }

                argList.Add(current);
                i++;
            }

            if (steps.Count > 0)
                list.Add(new JobAppTask(path, string.Join(" ", argList), interval, steps));
            else
                list.Add(new AppTask(path, string.Join(" ", argList), interval));
        }

        return list;
    }

    static async Task RunAppLoop(AppTask app)
    {
        string exeName = Path.GetFileName(app.Path);
        string exeNameWithArgs = $"{exeName} {app.Arguments}".Trim();

        do
        {
            Logger.Log($"STARTING {exeNameWithArgs}");

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = app.Path,
                        Arguments = app.Arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                lock (RunningProcesses)
                {
                    RunningProcesses.Add(process);
                }

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Logger.Log($"[OUTPUT] {exeNameWithArgs}: {e.Data}");
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Logger.Log($"[STDERR] {exeNameWithArgs}: {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());

                lock (RunningProcesses)
                {
                    RunningProcesses.Remove(process);
                }

                Logger.Log($"FINISHED {exeNameWithArgs} with exit code {process.ExitCode}");

                if (process.ExitCode != 0)
                    Logger.Log($"Non-zero exit code: {process.ExitCode}");

                if (app is JobAppTask jobApp)
                {
                    foreach (var step in jobApp.Steps)
                    {
                        if (step is RunSqlCommand sql)
                        {
                            var executor = new SqlJobExecutor(sql.ConnectionString);
                            Logger.Log($"Executing SQL command {exeName}: {sql.Command}");
                            await executor.ExecuteSqlCommandAsync(sql.Command);
                            Logger.Log($"SQL command executed successfully {exeName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"EXCEPTION while running {exeNameWithArgs}: {ex}");
            }

            if (app.Interval.HasValue)
            {
                Logger.Log($"Waiting {app.Interval.Value} minute(s) before next run of {exeNameWithArgs}");
                await Task.Delay(TimeSpan.FromMinutes(app.Interval.Value));
            }

        } while (app.Interval.HasValue);
    }
}