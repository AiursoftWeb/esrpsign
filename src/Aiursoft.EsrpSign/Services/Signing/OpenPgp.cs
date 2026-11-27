using System.Diagnostics;
using System.Text;
using Aiursoft.EsrpSign.SDK.Models;

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

    public static async Task<byte[]> Sign(string fingerprint, byte[] privateKey, Stream data,
        SignatureFormat format, CancellationToken cancellationToken)
    {
        var formatArguments = format switch
        {
            SignatureFormat.DetachedBinary => new[] { "--detach-sign" },
            SignatureFormat.DetachedArmored => new[] { "--armor", "--detach-sign" },
            SignatureFormat.ClearSigned => new[] { "--armor", "--clearsign" },
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported signature format.")
        };

        await using var gpg = new OpenPgp();
        using var key = new MemoryStream(privateKey, writable: false);
        await gpg.Run(key, cancellationToken, "--import");
        return await gpg.Run(data, cancellationToken, TimeSpan.FromMinutes(3),
            ["--digest-algo", "SHA256", "--local-user", fingerprint, "--output", "-", .. formatArguments]);
    }

    private Task<byte[]> Run(Stream? input, params string[] arguments) =>
        Run(input, CancellationToken.None, arguments);

    private Task<byte[]> Run(Stream? input, CancellationToken cancellationToken, params string[] arguments) =>
        Run(input, cancellationToken, TimeSpan.FromSeconds(30), arguments);

    private Task<byte[]> Run(Stream? input, CancellationToken cancellationToken, TimeSpan deadline, params string[] arguments) =>
        Execute("gpg", input, ["--homedir", _home, "--no-options", "--batch", "--yes", "--no-tty",
            "--pinentry-mode", "loopback", "--passphrase", "", .. arguments], cancellationToken, deadline);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Execute("gpgconf", null, ["--homedir", _home, "--kill", "gpg-agent"], CancellationToken.None,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private static async Task<byte[]> Execute(string executable, Stream? input, string[] arguments,
        CancellationToken cancellationToken, TimeSpan deadline)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
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
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

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
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (output.TryGetBuffer(out var buffer))
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer.AsSpan());
        }
    }

    private static async Task WriteInput(Stream stdin, Stream? input, CancellationToken cancellationToken)
    {
        await using (stdin)
        {
            if (input is not null)
                await input.CopyToAsync(stdin, cancellationToken);
        }
    }
}
