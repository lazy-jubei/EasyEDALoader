using System;
using System.IO;

namespace EasyEDA_Loader
{
    // Collects per-primitive import errors that were previously swallowed silently,
    // so failed footprint imports are visible and diagnosable.
    public static class ImportLog
    {
        private static readonly object Lock = new object();

        public static string LogPath { get; private set; }

        public static int ErrorCount { get; private set; }

        public static void Reset(string outputDirectory)
        {
            lock (Lock)
            {
                LogPath = Path.Combine(outputDirectory, "import-errors.log");
                ErrorCount = 0;
            }
        }

        public static void Error(string context, Exception ex)
        {
            lock (Lock)
            {
                ErrorCount++;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
                }
                catch
                {
                    // Logging must never break the import itself
                }
            }
        }
    }
}
