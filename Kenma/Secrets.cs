using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Kenma
{
    /// <summary>Stores API keys in Windows Credential Manager (encrypted per user), never in settings.json.</summary>
    static class Secrets
    {
        const uint CRED_TYPE_GENERIC = 1;
        const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        internal static string Get(string target)
        {
            try
            {
                if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr)) return "";
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                    if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return "";
                    var bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                    return Encoding.Unicode.GetString(bytes);
                }
                finally { CredFree(ptr); }
            }
            catch (Exception ex)
            {
                Log.Error("Reading API key from Credential Manager failed", ex);
                return "";
            }
        }

        /// <summary>Saves the secret, or deletes it when empty. Returns false if Windows refused.</summary>
        internal static bool Set(string target, string key)
        {
            key = key.Trim();
            if (key.Length == 0)
            {
                CredDelete(target, CRED_TYPE_GENERIC, 0);
                return true;
            }

            var bytes = Encoding.Unicode.GetBytes(key);
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = "Kenma"
                };
                if (CredWrite(ref cred, 0)) return true;
                Log.Error($"CredWrite failed, Win32 error {Marshal.GetLastWin32Error()}");
                return false;
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
                Marshal.FreeHGlobal(blob);
            }
        }
    }
}
