namespace Aiursoft.EsrpSign.SDK.Models;

public static class SignFileRequest
{
    public const int MaxFileBytes = 64 * 1024 * 1024;
    public const int MaxPassphraseBytes = 256;
    public const int MaxRequestBytes = MaxFileBytes + 4 * 1024;
}
