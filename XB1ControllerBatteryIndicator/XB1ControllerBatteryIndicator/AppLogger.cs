using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace XB1ControllerBatteryIndicator
{
    //small diagnostics log in %LOCALAPPDATA%\XB1ControllerBatteryIndicator, log.txt is moved to log.old.txt at 1 MB
    //Based on wefalltomorrow/XB1ControllerBatteryIndicator@922899b.
    internal static class AppLogger
    {
        private const long MaxLogBytes = 1024 * 1024;
        private static readonly object SyncRoot = new object();

        public static string LogDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XB1ControllerBatteryIndicator");

        private static readonly string FilePath = Path.Combine(LogDirectory, "log.txt");
        private static readonly string BackupPath = Path.Combine(LogDirectory, "log.old.txt");

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string context, Exception exception)
        {
            Write("ERROR", exception == null ? context : context + Environment.NewLine + exception);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= MaxLogBytes)
                    {
                        File.Move(FilePath, BackupPath, true);
                    }

                    var line = string.Format(CultureInfo.InvariantCulture, "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}",
                        DateTime.Now, level, WithoutUserProfile(message), Environment.NewLine);
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                //diagnostics must never affect the tray application itself
            }
        }

        //the log may be attached to a bug report, so it shouldn't contain the user name from a profile path, also not
        //inside exception messages. LOCALAPPDATA lies inside USERPROFILE, so it is replaced first
        private static string WithoutUserProfile(string message)
        {
            foreach (var (folder, variable) in new[]
                     {
                         (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
                         (Environment.SpecialFolder.UserProfile, "%USERPROFILE%"),
                     })
            {
                var path = Environment.GetFolderPath(folder);
                if (path.Length > 0)
                {
                    message = message.Replace(path, variable, StringComparison.OrdinalIgnoreCase);
                }
            }
            return message;
        }
    }
}
