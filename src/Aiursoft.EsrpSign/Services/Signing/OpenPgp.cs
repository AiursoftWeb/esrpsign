using System.Diagnostics;
using System.Text;

namespace Aiursoft.EsrpSign.Services.Signing;

sealed record GeneratedKey(string Fingerprint, byte[] PublicKey, byte[] PrivateKey);

sealed class OpenPgp : IAsyncDisposable
{
    private readonly string _home = CreateTemporaryHome();

    private static string CreateTemporaryHome()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Signing requires Linux with GnuPG and libargon2.");
        }

        return Directory.CreateDirectory(
            Path.Combine("/dev/shm", $"esrp-{Guid.NewGuid():N}"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute).FullName;
    }

    public static async Task<GeneratedKey> Create(Guid id)
    {
        await using var gpg = new OpenPgp();
        await gpg.Run(null, "--quick-generate-key", $"ESRP Sign {id:D}", "rsa3072", "sign", "0");
        var listing = await gpg.Run(null, "--with-colons", "--list-keys");
        var fingerprint = Encoding.UTF8.GetString(listing).Split('\n')
            .First(line => line.StartsWith("fpr:", StringComparison.Ordinal)).Split(':')[9];
        var publicKey = await gpg.Run(null, "--export", fingerprint);
        var privateKey = await gpg.Run(null, "--export-secret-keys", fingerprint);
        return new GeneratedKey(fingerprint, publicKey, privateKey);
    }

    public static async Task<byte[]> Sign(string fingerprint, byte[] privateKey, byte[] data)
    {
        await using var gpg = new OpenPgp();
        await gpg.Run(privateKey, "--import");
        return await gpg.Run(data, "--digest-algo", "SHA256", "--local-user", fingerprint,
            "--output", "-", "--detach-sign");
    }

    private Task<byte[]> Run(byte[]? input, params string[] arguments) =>
        Execute("gpg", input, ["--homedir", _home, "--no-options", "--batch", "--yes", "--no-tty",
            "--pinentry-mode", "loopback", "--passphrase", "", .. arguments]);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Execute("gpgconf", null, ["--homedir", _home, "--kill", "gpg-agent"]);
        }
        finally
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private static async Task<byte[]> Execute(string executable, byte[]? input, string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var output = new MemoryStream();
        try
        {
            var readOutput = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            var readError = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            var writeInput = WriteInput(process.StandardInput.BaseStream, input, timeout.Token);
            await Task.WhenAll(readOutput, readError, writeInput);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
            return output.ToArray();
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            if (output.TryGetBuffer(out var buffer))
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer.AsSpan());
        }
    }

    private static async Task WriteInput(Stream stdin, byte[]? input, CancellationToken cancellationToken)
    {
        await using (stdin)
        {
            if (input is not null)
                await stdin.WriteAsync(input, cancellationToken);
        }
    }
}
