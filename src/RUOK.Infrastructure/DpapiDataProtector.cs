using System.Security.Cryptography;
using System.Text;
using RUOK.Application;

namespace RUOK.Infrastructure;

public sealed class DpapiDataProtector : IDataProtector
{
    public byte[] Protect(byte[] plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        // Purpose binds the protected payload to its row; it is not a secret or an encryption key.
        return ProtectedData.Protect(plaintext, Encoding.UTF8.GetBytes(purpose), DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return ProtectedData.Unprotect(ciphertext, Encoding.UTF8.GetBytes(purpose), DataProtectionScope.CurrentUser);
    }
}
