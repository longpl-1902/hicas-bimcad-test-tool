using System;
using System.Diagnostics;
using System.IO;

namespace HicasTest.Bridge.Core
{
    /// <summary>File log at %LOCALAPPDATA%\HicasTest\logs\bridge-&lt;pid&gt;.log.</summary>
    public static class BridgeLog
    {
        private static readonly object Gate = new object();

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HicasTest", "logs",
            "bridge-" + Process.GetCurrentProcess().Id + ".log");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string context, Exception ex) => Write("ERROR", context + ": " + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, DateTime.Now.ToString("O") + " " + level + " " + message + Environment.NewLine);
                }
            }
            catch (IOException)
            {
                // Logging must never break the host; a locked or full disk only loses this line.
            }
        }
    }
}
