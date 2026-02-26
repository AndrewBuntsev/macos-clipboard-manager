namespace cbm;

/// <summary>
/// Simple logging utility to write informational messages to a log file in the user's home directory.
/// </summary>
public static class Log
{
    private static readonly string LogFile =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "cbm.log"
        );

    public static void Info(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        File.AppendAllText(LogFile, line + Environment.NewLine);

#if DEBUG
        System.Diagnostics.Debug.WriteLine(line);
#endif
    }
}
