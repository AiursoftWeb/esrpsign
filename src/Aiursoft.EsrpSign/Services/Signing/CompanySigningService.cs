using System.Security.Cryptography;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.EsrpSign.SDK.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.Services.Signing;

public sealed class CompanySigningService(
    TemplateDbContext db,
    PrivateKeys protection,
    ILogger<CompanySigningService> logger)
{
    public const string CryptoLimiter = "CompanySigning";

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

    public async Task<byte[]?> SignAsync(Guid id, string passphrase, byte[] data, string userId,
        CancellationToken cancellationToken)
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
            var signature = await OpenPgp.Sign(key.Fingerprint, privateKey, data);
            logger.LogInformation("User {UserId} signed with company key {SigningKeyId}.", userId, id);
            return signature;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }
}
