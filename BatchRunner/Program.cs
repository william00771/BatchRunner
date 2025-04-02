using System.Diagnostics;
using Microsoft.Data.SqlClient;
public record AppTask(string Path, string Arguments, int? Interval);

public abstract record SqlStep;
public record RunStoredProcedure(string Name) : SqlStep;
public record RunSqlCommand(string Command) : SqlStep;

public record JobAppTask(
    string Path,
    string Arguments,
    int? Interval,
    string? ConnectionString,
    List<SqlStep> Steps
) : AppTask(Path, Arguments, Interval);
class Program
{
    static string LogFilePath => Path.Combine(AppContext.BaseDirectory, "BatchRunner.log");
    static List<Process> RunningProcesses = new();

    static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("CTRL+C received. Terminating all child processes...");
            lock (RunningProcesses)
            {
                foreach (var p in RunningProcesses)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(true);
                            Console.WriteLine($"Killed {Path.GetFileName(p.StartInfo.FileName)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error killing process: {ex.Message}");
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

        Console.WriteLine("All apps started.");
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
            string? conn = null;
            var steps = new List<SqlStep>();

            while (i < args.Length && !args[i].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string current = args[i];

                if (current.Equals("-Interval", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int val))
                    {
                        interval = val;
                        i += 2;
                        continue;
                    }
                }
                if (current == "-ConnectionString" && i + 1 < args.Length)
                {
                    conn = args[++i];
                    i++;
                    continue;
                }
                if (current == "-RunStoredProcedure" && i + 1 < args.Length)
                {
                    steps.Add(new RunStoredProcedure(args[++i]));
                    i++;
                    continue;
                }
                if (current == "-RunSqlCommand" && i + 1 < args.Length)
                {
                    steps.Add(new RunSqlCommand(args[++i]));
                    i++;
                    continue;
                }

                argList.Add(current);
                i++;
            }

            if (steps.Count > 0 && !string.IsNullOrEmpty(conn))
            {
                list.Add(new JobAppTask(path, string.Join(" ", argList), interval, conn, steps));
            }
            else
            {
                list.Add(new AppTask(path, string.Join(" ", argList), interval));
            }
        }

        return list;
    }

    static async Task RunAppLoop(AppTask app)
    {
        string exeNameWithArgs = $"{Path.GetFileName(app.Path)} {app.Arguments}".Trim();

        do
        {
            string startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            LogToConsoleAndFile($"[{startTime}] STARTING {exeNameWithArgs}");

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
                        LogToConsoleAndFile($"[OUTPUT] {exeNameWithArgs}: {e.Data}");
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        LogToConsoleAndFile($"[STDERR] {exeNameWithArgs}: {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());

                lock (RunningProcesses)
                {
                    RunningProcesses.Remove(process);
                }

                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LogToConsoleAndFile($"[{endTime}] FINISHED {exeNameWithArgs} with exit code {process.ExitCode}");

                if (process.ExitCode != 0)
                    LogToConsoleAndFile($"[{endTime}] Non-zero exit code: {process.ExitCode}");

                if (app is JobAppTask jobApp)
                {
                    var jobExecutor = new SqlJobExecutor(jobApp.ConnectionString!);
                    string exeTag = $"[{Path.GetFileName(app.Path)}]";

                    foreach (var step in jobApp.Steps)
                    {
                        switch (step)
                        {
                            case RunStoredProcedure sp:
                                LogToConsoleAndFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exeTag} Executing stored procedure: {sp.Name}");
                                await jobExecutor.ExecuteStoredProcedureAsync(sp.Name);
                                LogToConsoleAndFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exeTag} Stored procedure {sp.Name} executed successfully.");
                                break;
                            case RunSqlCommand sql:
                                LogToConsoleAndFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exeTag} Executing SQL command: {sql.Command}");
                                await jobExecutor.ExecuteSqlCommandAsync(sql.Command);
                                LogToConsoleAndFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exeTag} SQL command executed successfully.");
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LogToConsoleAndFile($"[{errTime}] EXCEPTION while running {exeNameWithArgs}: {ex}");
            }

            if (app.Interval.HasValue)
            {
                string waitTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LogToConsoleAndFile($"[{waitTime}] Waiting {app.Interval.Value} minute(s) before next run of {exeNameWithArgs}");
                await Task.Delay(TimeSpan.FromSeconds(app.Interval.Value));
            }

        } while (app.Interval.HasValue);
    }

    static void LogToConsoleAndFile(string message)
    {
        Console.WriteLine(message);
        try
        {
            File.AppendAllText(LogFilePath, message + Environment.NewLine);
        }
        catch
        {
            Console.WriteLine("Failed to write to log file.");
        }
    }
}
class SqlJobExecutor
{
    private readonly string _connectionString;
    public SqlJobExecutor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task ExecuteSqlCommandAsync(string commandText)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await new SqlCommand(commandText, connection).ExecuteNonQueryAsync();
    }

    public async Task ExecuteStoredProcedureAsync(string jobName)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var startJobCmd = new SqlCommand("EXEC msdb.dbo.sp_start_job @job_name", connection);
        startJobCmd.Parameters.AddWithValue("@job_name", jobName);
        await startJobCmd.ExecuteNonQueryAsync();

        bool running;
        do
        {
            await Task.Delay(3000);
            var checkJobCmd = new SqlCommand(@"
                SELECT COUNT(*) 
                FROM msdb.dbo.sysjobactivity a 
                JOIN msdb.dbo.sysjobs b ON a.job_id = b.job_id 
                WHERE b.name = @job_name AND stop_execution_date IS NULL", connection);
            checkJobCmd.Parameters.AddWithValue("@job_name", jobName);

            running = (int)await checkJobCmd.ExecuteScalarAsync() > 0;
        } while (running);
    }
}