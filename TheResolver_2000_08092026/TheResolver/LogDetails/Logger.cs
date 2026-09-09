using System;
using System.IO;


public static class Logger
{
    private static readonly string LogFile =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.Desktop),
            "CableTrayResolver.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}"
                + Environment.NewLine
                + Environment.NewLine);
        }
        catch
        {
            // Ignore logging failures
        }
    }
}
