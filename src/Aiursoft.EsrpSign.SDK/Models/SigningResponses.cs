using Aiursoft.AiurProtocol.Models;

namespace Aiursoft.EsrpSign.SDK.Models;

public sealed class SigningKeyResponse : AiurResponse
{
    public required SigningKeyDto Key { get; set; }
}

public sealed class PublicKeyResponse : AiurResponse
{
    public required byte[] PublicKey { get; set; }
}

public sealed class SignResponse : AiurResponse
{
    public required byte[] Signature { get; set; }
}
