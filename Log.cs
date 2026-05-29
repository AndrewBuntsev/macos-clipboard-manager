namespace cbm;

/// <summary>
/// Simple logging utility with size-based rotation under the user's Library/Logs folder.
/// </summary>
public static class Log
{
    private const long MAX_LOG_FILE_BYTES = 2L * 1024 * 1024;
    private const int MAX_LOG_FILES = 5;
    private static readonly object Sync = new();

    private static readonly string LogDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Logs",
            "cbm"
        );
    private static readonly string LogFile = Path.Combine(LogDirectory, "cbm.log");

    public static void Info(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded(line);
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[log failure] {ex.Message}");
#endif
            }
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine(line);
#endif
    }

    private static void RotateIfNeeded(string nextLine)
    {
        if (!File.Exists(LogFile))
            return;

        var nextLineBytes = System.Text.Encoding.UTF8.GetByteCount(
            nextLine + Environment.NewLine
        );
        if (new FileInfo(LogFile).Length + nextLineBytes <= MAX_LOG_FILE_BYTES)
            return;

        var maxRotatedIndex = MAX_LOG_FILES - 1;
        var oldestLogFile = RotatedLogFile(maxRotatedIndex);
        if (File.Exists(oldestLogFile))
            File.Delete(oldestLogFile);

        for (var i = maxRotatedIndex - 1; i >= 1; i--)
        {
            var source = RotatedLogFile(i);
            if (File.Exists(source))
                File.Move(source, RotatedLogFile(i + 1));
        }

        File.Move(LogFile, RotatedLogFile(1));
    }

    private static string RotatedLogFile(int index) =>
        Path.Combine(LogDirectory, $"cbm.{index}.log");
}
