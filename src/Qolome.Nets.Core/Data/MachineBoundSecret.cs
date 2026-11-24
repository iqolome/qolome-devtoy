using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Qolome.Nets.Core.Data;

/// <summary>
/// 用本机主板等信息派生密钥，加密 MySQL 密码。换设备后无法解密，需重新输入。
/// </summary>
public static class MachineBoundSecret
{
    public const string Prefix = "qn1.";
    private const string Purpose = "qolome-nets/mysql-password/v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly AsyncLocal<int> OmitPasswordDepth = new();
    private static readonly object FingerprintLock = new();
    private static string? _fingerprint;
    private static byte[]? _key;

    public static bool LastUnprotectFailed { get; private set; }
    public static bool LastWasLegacyPlaintext { get; private set; }

    public static bool IsOmittingPassword => OmitPasswordDepth.Value > 0;

    public static IDisposable OmitPassword() => new OmitScope();

    public static string ToStorage(string? plaintext)
    {
        if (IsOmittingPassword)
        {
            return "";
        }

        if (string.IsNullOrEmpty(plaintext))
        {
            return "";
        }

        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(Purpose));

        var packed = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, packed, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, packed, NonceSize + cipher.Length, TagSize);
        CryptographicOperations.ZeroMemory(plain);
        return Prefix + Convert.ToBase64String(packed);
    }

    public static string FromStorage(string? stored)
    {
        LastUnprotectFailed = false;
        LastWasLegacyPlaintext = false;
        if (string.IsNullOrEmpty(stored))
        {
            return "";
        }

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            LastWasLegacyPlaintext = true;
            return stored;
        }

        try
        {
            var packed = Convert.FromBase64String(stored[Prefix.Length..]);
            if (packed.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("ciphertext too short");
            }

            var nonce = packed.AsSpan(0, NonceSize);
            var tag = packed.AsSpan(packed.Length - TagSize, TagSize);
            var cipher = packed.AsSpan(NonceSize, packed.Length - NonceSize - TagSize);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(GetKey(), TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(Purpose));
            var text = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return text;
        }
        catch
        {
            LastUnprotectFailed = true;
            return "";
        }
    }

    private static byte[] GetKey()
    {
        lock (FingerprintLock)
        {
            if (_key is not null)
            {
                return _key;
            }

            var fingerprint = ReadMotherboardFingerprint();
            _fingerprint = fingerprint;
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose + "\n" + fingerprint));
            return _key;
        }
    }

    private static string ReadMotherboardFingerprint()
    {
        var board = QueryWmi("Win32_BaseBoard", "Manufacturer", "Product", "SerialNumber");
        var uuid = QueryWmi("Win32_ComputerSystemProduct", "UUID");
        var bios = QueryWmi("Win32_BIOS", "Manufacturer", "SerialNumber");
        var registry = ReadBiosRegistry();
        var combined = string.Join("|", new[] { board, uuid, bios, registry }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(combined) ? "unknown-board" : combined;
    }

    private static string? QueryWmi(string className, params string[] properties)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", properties)} FROM {className}");
            searcher.Options.Timeout = TimeSpan.FromSeconds(4);
            foreach (var raw in searcher.Get())
            {
                using var obj = (ManagementObject)raw;
                var parts = properties
                    .Select(name => obj[name]?.ToString()?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value!));
                var joined = string.Join("|", parts);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return $"{className}:{joined}";
                }
            }
        }
        catch
        {
            // 无 WMI 权限或类不存在时走注册表回退。
        }

        return null;
    }

    private static string? ReadBiosRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (key is null)
            {
                return null;
            }

            string[] names =
            [
                "BaseBoardManufacturer", "BaseBoardProduct", "BaseBoardVersion",
                "SystemManufacturer", "SystemProductName", "SystemSKU"
            ];
            var parts = names
                .Select(name => key.GetValue(name)?.ToString()?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value!));
            var joined = string.Join("|", parts);
            return string.IsNullOrWhiteSpace(joined) ? null : "BIOS:" + joined;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPlaceholder(string value)
    {
        var text = value.Trim();
        return text.Length == 0
            || text.Equals("None", StringComparison.OrdinalIgnoreCase)
            || text.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Default string", StringComparison.OrdinalIgnoreCase)
            || text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
            || text.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
            || text.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase)
            || text.Equals("00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OmitScope : IDisposable
    {
        public OmitScope() => OmitPasswordDepth.Value++;

        public void Dispose()
        {
            if (OmitPasswordDepth.Value > 0)
            {
                OmitPasswordDepth.Value--;
            }
        }
    }
}
