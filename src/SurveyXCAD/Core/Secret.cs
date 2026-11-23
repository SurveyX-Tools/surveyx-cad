using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SurveyX.Core
{
    /// <summary>Encrypts small secrets (the API key) for the current Windows user with DPAPI. No extra packages needed.</summary>
    public static class Secret
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] data = Encoding.UTF8.GetBytes(plain);
            var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            var outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);
                if (!CryptProtectData(ref inBlob, "SurveyXCAD", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    throw new InvalidOperationException("Could not protect the key (DPAPI error " + Marshal.GetLastWin32Error() + ").");
                var res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, outBlob.cbData);
                return Convert.ToBase64String(res);
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        public static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            byte[] data;
            try { data = Convert.FromBase64String(b64); } catch { return ""; }
            var inBlob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            var outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)) return "";
                var res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, outBlob.cbData);
                return Encoding.UTF8.GetString(res);
            }
            catch { return ""; }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
    }
}
