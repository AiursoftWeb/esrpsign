namespace Aiursoft.EsrpSign.SDK.Models;

public sealed record SigningKeyDto(Guid Id, string Name, string Fingerprint, DateTime CreatedAt);
