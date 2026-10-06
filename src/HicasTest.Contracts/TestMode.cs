using System;

namespace HicasTest.Contracts
{
    /// <summary>True only in a host started by HicasTest (the bridge sets HICASTEST_MODE=1 inside that process).</summary>
    public static class TestMode
    {
        public const string VariableName = "HICASTEST_MODE";

        public static bool IsActive
        {
            get { return Environment.GetEnvironmentVariable(VariableName) == "1"; }
        }
    }
}
