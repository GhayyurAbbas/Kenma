using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Kenma.Tests
{
    static class TestSetup
    {
        /// <summary>Runs before any test touches the app's types, so test logs never land in the user's real log.</summary>
        [ModuleInitializer]
        internal static void Init() =>
            Environment.SetEnvironmentVariable("KENMA_LOG_DIR", Path.Combine(Path.GetTempPath(), "kenma-test-logs"));
    }
}
