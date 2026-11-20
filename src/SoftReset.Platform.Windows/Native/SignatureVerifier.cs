using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using SoftReset.Core.Models;

namespace SoftReset.Platform.Windows.Native;

/// <summary>
/// Verifies Authenticode signatures with WinVerifyTrust. Checks the embedded signature first and,
/// if there is none, the system catalog database (most Windows binaries are catalog-signed).
/// Revocation is checked from cache only, so verification never goes online.
/// </summary>
internal static class SignatureVerifier
{
    private static readonly ConcurrentDictionary<string, (SignatureState State, string? Signer, bool Catalog)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_CHOICE_CATALOG = 2;
    private const uint WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000, WTD_REVOCATION_CHECK_NONE = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        public IntPtr pcwszCatalogFilePath;
        public IntPtr pcwszMemberTag;
        public IntPtr pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pInfo;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CATALOG_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string wszCatalogFile;
    }

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, in Guid action, ref WINTRUST_DATA data);

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr catAdmin, IntPtr subsystem, string? hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr catAdmin, IntPtr file, ref uint hashSize, byte[]? hash, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr catAdmin, byte[] hash, uint hashSize, uint flags, IntPtr prevCatInfo);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr catInfo, ref CATALOG_INFO info, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catInfo, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);

    public static (SignatureState State, string? Signer, bool Catalog) Verify(string path)
    {
        if (string.IsNullOrEmpty(path)) return (SignatureState.NotChecked, null, false);
        if (Cache.TryGetValue(path, out var cached)) return cached;
        (SignatureState, string?, bool) result;
        try
        {
            if (!File.Exists(path)) result = (SignatureState.FileNotFound, null, false);
            else
            {
                var embedded = VerifyEmbedded(path);
                if (embedded == SignatureState.Unsigned)
                {
                    var (catState, catalogFile) = VerifyCatalog(path);
                    result = catState == SignatureState.Unsigned
                        ? (SignatureState.Unsigned, null, false)
                        : (Classify(catState, SignerOf(catalogFile)), SignerOf(catalogFile), true);
                }
                else
                {
                    var signer = embedded is SignatureState.Signed or SignatureState.Expired ? SignerOf(path) : null;
                    result = (Classify(embedded, signer), signer, false);
                }
            }
        }
        catch (UnauthorizedAccessException) { result = (SignatureState.AccessDenied, null, false); }
        catch { result = (SignatureState.NotChecked, null, false); }
        Cache[path] = result;
        return result;
    }

    private static SignatureState Classify(SignatureState s, string? signer) =>
        s == SignatureState.Signed && signer is not null && (signer.Equals("Microsoft Windows", StringComparison.OrdinalIgnoreCase) || signer.StartsWith("Microsoft Windows ", StringComparison.OrdinalIgnoreCase))
            ? SignatureState.SignedMicrosoftWindows
            : s;

    private static SignatureState Map(int hr) => unchecked((uint)hr) switch
    {
        0 => SignatureState.Signed,
        0x800B0100 => SignatureState.Unsigned,          // TRUST_E_NOSIGNATURE
        0x800B0003 => SignatureState.Unsigned,          // TRUST_E_SUBJECT_FORM_UNKNOWN
        0x800B0001 => SignatureState.Unsigned,          // TRUST_E_PROVIDER_UNKNOWN
        0x800B0101 => SignatureState.Expired,           // CERT_E_EXPIRED
        0x800B010C => SignatureState.Revoked,           // CERT_E_REVOKED
        0x80070005 => SignatureState.AccessDenied,
        _ => SignatureState.InvalidOrUntrusted,         // bad digest, untrusted root, explicit distrust
    };

    private static SignatureState VerifyEmbedded(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = pathPtr };
        var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, infoPtr, false);
            var data = NewData(WTD_CHOICE_FILE, infoPtr);
            var hr = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            return Map(hr);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    private static (SignatureState, string?) VerifyCatalog(string path)
    {
        foreach (var alg in new[] { "SHA256", null })
        {
            if (!CryptCATAdminAcquireContext2(out var catAdmin, IntPtr.Zero, alg, IntPtr.Zero, 0)) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var handle = fs.SafeFileHandle.DangerousGetHandle();
                uint size = 0;
                CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref size, null, 0);
                if (size == 0 || size > 256) continue;
                var hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref size, hash, 0)) continue;
                var catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, hash, size, 0, IntPtr.Zero);
                if (catInfo == IntPtr.Zero) continue;
                try
                {
                    var ci = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf<CATALOG_INFO>() };
                    if (!CryptCATCatalogInfoFromContext(catInfo, ref ci, 0)) continue;
                    var tag = Convert.ToHexString(hash);
                    var catPath = Marshal.StringToHGlobalUni(ci.wszCatalogFile);
                    var tagPtr = Marshal.StringToHGlobalUni(tag);
                    var memberPath = Marshal.StringToHGlobalUni(path);
                    var hashPtr = Marshal.AllocHGlobal(hash.Length);
                    Marshal.Copy(hash, 0, hashPtr, hash.Length);
                    var wci = new WINTRUST_CATALOG_INFO
                    {
                        cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(),
                        pcwszCatalogFilePath = catPath,
                        pcwszMemberTag = tagPtr,
                        pcwszMemberFilePath = memberPath,
                        hMemberFile = handle,
                        pbCalculatedFileHash = hashPtr,
                        cbCalculatedFileHash = size,
                        hCatAdmin = catAdmin,
                    };
                    var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_CATALOG_INFO>());
                    try
                    {
                        Marshal.StructureToPtr(wci, infoPtr, false);
                        var data = NewData(WTD_CHOICE_CATALOG, infoPtr);
                        var hr = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
                        data.dwStateAction = WTD_STATEACTION_CLOSE;
                        WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
                        return (Map(hr), ci.wszCatalogFile);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(infoPtr);
                        Marshal.FreeHGlobal(catPath);
                        Marshal.FreeHGlobal(tagPtr);
                        Marshal.FreeHGlobal(memberPath);
                        Marshal.FreeHGlobal(hashPtr);
                    }
                }
                finally { CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0); }
            }
            catch (IOException) { }
            finally { CryptCATAdminReleaseContext(catAdmin, 0); }
        }
        return (SignatureState.Unsigned, null);
    }

    private static WINTRUST_DATA NewData(uint choice, IntPtr info) => new()
    {
        cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
        dwUIChoice = WTD_UI_NONE,
        fdwRevocationChecks = WTD_REVOKE_NONE,
        dwUnionChoice = choice,
        pInfo = info,
        dwStateAction = WTD_STATEACTION_VERIFY,
        dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE,
    };

#pragma warning disable SYSLIB0057
    private static string? SignerOf(string? signedFile)
    {
        if (string.IsNullOrEmpty(signedFile)) return null;
        try
        {
            using var cert = X509Certificate.CreateFromSignedFile(signedFile);
            using var c2 = new X509Certificate2(cert);
            return c2.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch { return null; }
    }
#pragma warning restore SYSLIB0057
}
