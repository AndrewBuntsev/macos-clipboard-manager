using System;
using System.IO;

namespace cbm;

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
