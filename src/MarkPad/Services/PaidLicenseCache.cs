using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MarkPad.Services;

public interface IPaidLicenseCache
{
    bool HasConfirmedPurchase();
    void SaveConfirmedPurchase();
}

/// <summary>A Windows-user protected record of a previously confirmed full Store purchase.</summary>
public sealed class PaidLicenseCache : IPaidLicenseCache
{
    private readonly string _path;
    private readonly byte[] _proof;
    private readonly byte[] _entropy;

    public PaidLicenseCache(string directory, string packageFamily, string productId)
    {
        _entropy = Encoding.UTF8.GetBytes("Hanqing.PaidLicense.v1\0" + packageFamily + "\0" + productId);
        _proof = Encoding.UTF8.GetBytes("StoreConfirmedFullPurchase.v1\0" + packageFamily + "\0" + productId);
        _path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(_entropy)) + ".dat");
    }

    public bool HasConfirmedPurchase()
    {
        if (!File.Exists(_path)) return false;
        // A damaged or substituted record is never authority to enter the editor.
        if (new FileInfo(_path).Length > 16 * 1024) return false;
        var proof = ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy, DataProtectionScope.CurrentUser);
        return CryptographicOperations.FixedTimeEquals(proof, _proof);
    }

    public void SaveConfirmedPurchase()
    {
        var bytes = ProtectedData.Protect(_proof, _entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        AtomicFile.Write(_path, bytes);
    }
}
