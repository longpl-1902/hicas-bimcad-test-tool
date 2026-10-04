using System;
using System.IO;
using HicasTest.Protocol;

namespace HicasTest.Bridge.Core
{
    /// <summary>%LOCALAPPDATA%\HicasTest\bridges\&lt;pid&gt;.json announces a running bridge to the runner.</summary>
    public static class DiscoveryFile
    {
        public static string Directory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HicasTest", "bridges");

        public static string PathFor(int pid) => Path.Combine(Directory, pid + ".json");

        public static void Write(BridgeInfo info)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var target = PathFor(info.Pid);
            var temp = target + ".tmp";
            File.WriteAllText(temp, JsonCodec.Serialize(info));
            if (File.Exists(target))
                File.Delete(target);
            File.Move(temp, target);
        }

        public static void Delete(int pid)
        {
            var path = PathFor(pid);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
