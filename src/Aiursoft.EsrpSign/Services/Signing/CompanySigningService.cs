using System.Security.Cryptography;
using System.Text;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.EsrpSign.SDK.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.Services.Signing;

public enum PassphraseVerificationOutcome
{
    NotFound,
    Valid,
    InvalidPassphrase,
    KeyRingUnavailable
}

public sealed class CompanySigningService(
    TemplateDbContext db,
    PrivateKeys protection,
    ILogger<CompanySigningService> logger)
{
    public const string CryptoLimiter = "CompanySigning";

    public static string FormatFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return string.Empty;
        }

        var compact = fingerprint.Trim();
        var builder = new StringBuilder(compact.Length + compact.Length / 4);
        for (var i = 0; i < compact.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(compact[i]);
        }

        return builder.ToString();
    }

    public Task<List<SigningKeyDto>> ListAsync(CancellationToken cancellationToken) =>
        db.CompanySigningKeys.AsNoTracking()
            .OrderByDescending(key => key.CreatedAt).ThenBy(key => key.Id)
            .Select(key => new SigningKeyDto(key.Id, key.Name, key.Fingerprint, key.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<SigningKeyDto?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.CompanySigningKeys.AsNoTracking().Where(key => key.Id == id)
            .Select(key => new SigningKeyDto(key.Id, key.Name, key.Fingerprint, key.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<byte[]?> GetPublicKeyAsync(Guid id, CancellationToken cancellationToken) =>
        db.CompanySigningKeys.AsNoTracking().Where(key => key.Id == id)
            .Select(key => key.PublicKey).SingleOrDefaultAsync(cancellationToken);

    public async Task<SigningKeyDto> CreateAsync(string name, string passphrase, string userId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var generated = await OpenPgp.Create(id);
        try
        {
            var key = new CompanySigningKey
            {
                Id = id,
                Name = name.Trim(),
                CreatedAt = DateTime.UtcNow,
                Fingerprint = generated.Fingerprint,
                PublicKey = generated.PublicKey,
                PrivateEnvelope = protection.Seal(id, passphrase, generated.PrivateKey)
            };
            db.CompanySigningKeys.Add(key);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("User {UserId} created company signing key {SigningKeyId}.", userId, id);
            return new SigningKeyDto(key.Id, key.Name, key.Fingerprint, key.CreatedAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(generated.PrivateKey);
        }
    }

    public async Task<PassphraseVerificationOutcome> VerifyPassphraseAsync(Guid id, string passphrase, string userId,
        CancellationToken cancellationToken)
    {
        var envelope = await db.CompanySigningKeys.AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => candidate.PrivateEnvelope)
            .SingleOrDefaultAsync(cancellationToken);
        if (envelope is null)
        {
            return PassphraseVerificationOutcome.NotFound;
        }

        try
        {
            var privateKey = protection.Open(id, passphrase, envelope);
            CryptographicOperations.ZeroMemory(privateKey);
            logger.LogInformation("User {UserId} verified the passphrase for company key {SigningKeyId}: Valid.", userId, id);
            return PassphraseVerificationOutcome.Valid;
        }
        catch (AuthenticationTagMismatchException)
        {
            logger.LogWarning("User {UserId} failed passphrase verification for company key {SigningKeyId}: InvalidPassphrase.", userId, id);
            return PassphraseVerificationOutcome.InvalidPassphrase;
        }
        catch (CryptographicException)
        {
            logger.LogWarning("User {UserId} failed passphrase verification for company key {SigningKeyId}: KeyRingUnavailable.", userId, id);
            return PassphraseVerificationOutcome.KeyRingUnavailable;
        }
    }

    public async Task<byte[]?> SignAsync(Guid id, string passphrase, byte[] data, string userId,
        CancellationToken cancellationToken, SignatureFormat format = SignatureFormat.DetachedBinary)
    {
        using var input = new MemoryStream(data, writable: false);
        return await SignAsync(id, passphrase, input, userId, cancellationToken, format);
    }

    public async Task<byte[]?> SignAsync(Guid id, string passphrase, Stream data, string userId,
        CancellationToken cancellationToken, SignatureFormat format = SignatureFormat.DetachedBinary)
    {
        var key = await db.CompanySigningKeys.AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.PrivateEnvelope, candidate.Fingerprint })
            .SingleOrDefaultAsync(cancellationToken);
        if (key is null)
        {
            return null;
        }

        var privateKey = protection.Open(id, passphrase, key.PrivateEnvelope);
        try
        {
            var signature = await OpenPgp.Sign(key.Fingerprint, privateKey, data, format, cancellationToken);
            logger.LogInformation("User {UserId} signed with company key {SigningKeyId} using {SignatureFormat}.",
                userId, id, format);
            return signature;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }
}
