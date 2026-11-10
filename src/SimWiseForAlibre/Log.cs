using System;
using System.IO;

namespace SimWiseForAlibre
{
    // %LOCALAPPDATA%\SimWiseForAlibre\SimWiseForAlibre.log, one line per entry: time [area] text.
    static class Log
    {
        static readonly object s_lock = new object();

        public static string FilePath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimWiseForAlibre");
                return Path.Combine(folder, "SimWiseForAlibre.log");
            }
        }

        public static void Write(string area, string text)
        {
            try
            {
                lock (s_lock)
                {
                    string path = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                                             " [" + area + "] " + text + "\r\n");
                }
            }
            catch
            {
                // Logging must never break Alibre.
            }
        }
    }
}
