using System;
using System.IO;
using System.Net.NetworkInformation;

namespace Kenma.Engines
{
    static class Connectivity
    {
        /// <summary>Raised (on a background thread) when Windows reports the network going up or down.</summary>
        public static event Action? Changed;

        static Connectivity()
        {
            NetworkChange.NetworkAvailabilityChanged += (_, _) => Changed?.Invoke();
            NetworkChange.NetworkAddressChanged += (_, _) => Changed?.Invoke();
        }

        /// <summary>
        /// Fast check that some non-loopback network is up. It can't prove the internet is reachable,
        /// but it lets us skip cloud engines instantly when the PC is clearly offline.
        /// </summary>
        public static bool IsOnline
        {
            get
            {
                try { return NetworkInterface.GetIsNetworkAvailable(); }
                catch { return true; }
            }
        }

        static (DateTime at, bool found) _claudeCache;

        /// <summary>Whether the claude CLI is on PATH (cached for a minute; no process is started).</summary>
        public static bool ClaudeCliInstalled
        {
            get
            {
                if (DateTime.UtcNow - _claudeCache.at < TimeSpan.FromMinutes(1)) return _claudeCache.found;
                bool found = false;
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        if (File.Exists(Path.Combine(dir, "claude.cmd")) || File.Exists(Path.Combine(dir, "claude.exe"))) { found = true; break; }
                    }
                    catch { }
                }
                _claudeCache = (DateTime.UtcNow, found);
                return found;
            }
        }
    }
}
