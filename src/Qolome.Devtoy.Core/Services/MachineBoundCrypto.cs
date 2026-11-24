using System.Security.Cryptography;
using System.Text;

namespace Qolome.Devtoy.Core.Services;

/// <summary>本机派生密钥的 AES-GCM（连接配置 .dat 等）。换机后无法解密。</summary>
public static class MachineBoundCrypto
{
    private const string Purpose = "qolome-devtoy/datasource/v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly object KeyLock = new();
    private static byte[]? _key;

    public static byte[] Protect(byte[] plaintext)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, cipher, tag, Encoding.UTF8.GetBytes(Purpose));

        var packed = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, packed, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, packed, NonceSize + cipher.Length, TagSize);
        return packed;
    }

    public static byte[]? Unprotect(byte[] packed)
    {
        if (packed.Length < NonceSize + TagSize)
        {
            return null;
        }

        try
        {
            var key = GetKey();
            var nonce = packed.AsSpan(0, NonceSize);
            var tag = packed.AsSpan(packed.Length - TagSize, TagSize);
            var cipher = packed.AsSpan(NonceSize, packed.Length - NonceSize - TagSize);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(Purpose));
            return plain;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] GetKey()
    {
        lock (KeyLock)
        {
            if (_key is not null)
            {
                return _key;
            }

            var fingerprint = string.Join(
                "|",
                Environment.MachineName,
                Environment.UserName,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose + "\n" + fingerprint));
            return _key;
        }
    }
}
