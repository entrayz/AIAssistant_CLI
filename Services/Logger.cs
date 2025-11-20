using System;
using System.IO;

namespace ICTVisualizer.Services;

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "zai.log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message)
    {
        Write("ERROR", message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level}: {message}" + Environment.NewLine;
            lock (_lock)
            {
                File.AppendAllText(_path, line);
                // Also echo to console so immediate runs (dotnet run) show messages when file might be elsewhere
                try
                {
                    Console.Write(line);
                }
                catch
                {
                    // ignore console failures
                }
            }
        }
        catch
        {
            // ignore logging failures
        }
    }
}
