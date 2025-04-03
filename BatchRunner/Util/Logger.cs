using System;
using System.IO;

namespace BatchRunner.Util;

public static class Logger
{
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "BatchRunner.log");

    public static void Log(string message)
    {
        string timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(timestamped);
        try
        {
            File.AppendAllText(LogFilePath, timestamped + Environment.NewLine);
        }
        catch
        {
            Console.WriteLine("Failed to write to log file.");
        }
    }
}
