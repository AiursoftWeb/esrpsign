using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Aiursoft.EsrpSign.Services.Signing;

public sealed class PrivateKeys(IDataProtectionProvider provider)
{
    // The protector purpose identifies the fixed v1 envelope and binds it to the key ID.
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = SaltSize + NonceSize + TagSize;

    public static bool IsValidPassphrase(string? value) =>
        value is { Length: >= 12 } && Encoding.UTF8.GetByteCount(value) <= 256;

    public byte[] Seal(Guid id, string passphrase, byte[] privateKey)
    {
        var envelope = new byte[HeaderSize + privateKey.Length];
        var salt = envelope.AsSpan(0, SaltSize);
        var nonce = envelope.AsSpan(SaltSize, NonceSize);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        var derivedKey = DeriveKey(passphrase, salt);
        try
        {
            using var aes = new AesGcm(derivedKey, TagSize);
            aes.Encrypt(nonce, privateKey, envelope.AsSpan(HeaderSize),
                envelope.AsSpan(SaltSize + NonceSize, TagSize), id.ToByteArray());
            return Protector(id).Protect(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public byte[] Open(Guid id, string passphrase, byte[] protectedEnvelope)
    {
        var envelope = Protector(id).Unprotect(protectedEnvelope);
        if (envelope.Length <= HeaderSize)
            throw new CryptographicException("Invalid private key envelope.");

        var derivedKey = DeriveKey(passphrase, envelope.AsSpan(0, SaltSize));
        var privateKey = new byte[envelope.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(derivedKey, TagSize);
            aes.Decrypt(envelope.AsSpan(SaltSize, NonceSize), envelope.AsSpan(HeaderSize),
                envelope.AsSpan(SaltSize + NonceSize, TagSize), privateKey, id.ToByteArray());
            return privateKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(privateKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    private IDataProtector Protector(Guid id) =>
        provider.CreateProtector("Aiursoft.EsrpSign.PrivateKey.v1", id.ToString("D"));

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt)
    {
        var password = Encoding.UTF8.GetBytes(passphrase);
        var key = new byte[32];
        try
        {
            var status = Argon2IdHashRaw(3, 65_536, 4, password, (nuint)password.Length,
                salt.ToArray(), SaltSize, key, (nuint)key.Length);
            if (status != 0)
                throw new CryptographicException($"Argon2id failed ({status}).");
            return key;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    [DllImport("libargon2.so.1", EntryPoint = "argon2id_hash_raw", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Argon2IdHashRaw(uint time, uint memoryKiB, uint lanes, byte[] password,
        nuint passwordLength, byte[] salt, nuint saltLength, [Out] byte[] key, nuint keyLength);
}
