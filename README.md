# Aiursoft ESRP Sign

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![Pipeline stat](https://badge-proxy.aiursoft.com/aiursoft/esrpsign/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/esrpsign/-/pipelines)
[![Test Coverage](https://badge-proxy.aiursoft.com/aiursoft/esrpsign/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/esrpsign/-/pipelines)
[![S360 compliance](https://s360.aiursoft.com/badge.svg?project=aiursoft%2Fesrpsign)](https://s360.aiursoft.com/SupplyChain?tab=projects&project=aiursoft%2Fesrpsign&scope=baseline&ecosystem=all)

## Overview

### What is ESRP Sign?

ESRP Sign creates and protects company OpenPGP signing keys. Employees with permission can create keys or sign with an existing key's passphrase. Anyone can download a public key to verify a release. The web application uses the Aiursoft Template for its UI, authentication, permissions, and database providers.

## SDK

The `Aiursoft.EsrpSign.SDK` project provides an AiurProtocol client for .NET. The client does not retry key creation or signing requests automatically.

```csharp
using Aiursoft.EsrpSign.SDK;
using Aiursoft.EsrpSign.SDK.Models;

services.AddEsrpSignSdk("https://sign.example.com");

// Resolve EsrpSignApiClient from the application's service scope.
var result = await client.SignAsync(apiKey, signingKeyId, new SignRequest
{
    Passphrase = passphrase,
    Data = await File.ReadAllBytesAsync("SHA256SUMS")
});
await File.WriteAllBytesAsync("SHA256SUMS.sig", result.Signature);
```

## Run manually

1. Install the .NET 10 SDK, Node.js, GnuPG, and `libargon2-1` on Linux. The .NET build prepares the frontend assets with npm.
2. Run `dotnet run --project src/Aiursoft.EsrpSign -- --urls http://localhost:5000`.
3. Open [http://localhost:5000](http://localhost:5000). For local Development, the default administrator is `admin@default.com` with password `Admin@123456!`.

## Run in Microsoft Visual Studio

1. Open `Aiursoft.EsrpSign.sln`.
2. Run the web project with the Linux or WSL development environment defined above.

## Run in Docker

Build the image from this source tree. Set the initial administrator password in your shell before starting the application against a new database.

```bash
docker build -t esrpsign .
docker run --name esrpsign -p 127.0.0.1:5000:5000 \
  --env AppSettings__Local__InitialAdminPassword \
  --mount type=volume,source=esrpsign-data,target=/data esrpsign
```

Open [http://localhost:5000](http://localhost:5000). The container uses GnuPG and Argon2 from the base distribution.

## Usage

### Company signing keys

Grant **Create Company Signing Keys** and **Sign with Company Keys** to the appropriate roles in the permissions administration. These are independent grants. In **Company Signing Keys**, authorized users can create a key with a passphrase or download a detached signature for UTF-8 text. Keys are shared company assets and remain available when the creator's account is removed.

Anyone can browse public key fingerprints and download the binary OpenPGP public keys. Use `gpg --verify SHA256SUMS.sig SHA256SUMS` after importing the intended public key and checking its fingerprint through a trusted channel.

### Personal API Keys

Create a personal API key from **Settings → API Keys** and copy it when it is shown. The server stores a hash. Requests use `Authorization: Bearer <personal-api-key>`. Expiration, revocation, account lockout, and changes to the owner's local application permissions are checked for each protected request.

| Request | Authorization | Body or result |
| --- | --- | --- |
| `GET /api/v1/signing-keys` | Public | Key ID, name, fingerprint and creation time |
| `GET /api/v1/signing-keys/{id}/public-key` | Public | Base64-encoded public key in the AiurProtocol response |
| `POST /api/v1/signing-keys` | Personal API Key and create permission | JSON with `name` and `passphrase` |
| `POST /api/v1/signing-keys/{id}/sign` | Personal API Key and sign permission | JSON with `passphrase` and Base64 `data`; returns a Base64 OpenPGP detached signature |

The signature input may be up to 64 KiB. This suits release manifests such as `SHA256SUMS`. Browser text uses UTF-8 with LF line endings. Files sent using the SDK are signed byte for byte.

## Configuration

| Setting | Purpose |
| --- | --- |
| `AppSettings__Local__InitialAdminPassword` | Bootstrap password for a new database outside Development. Use a unique password of at least 12 characters that meets the account password policy. |
| `AppSettings__AuthProvider` | `Local` by default. Select `OIDC` and configure its authority and client settings for corporate sign-in. |
| `ConnectionStrings__DbType` | `Sqlite` by default. The application also supports `MySql`. |
| `Storage__Path` | Persistent data path. The Docker image uses `/data`. |
| `Signing__DataProtectionKeyPath` | Override the Data Protection key ring path. The default follows Template and is `/data/.aspnet/DataProtection-Keys` in Docker. |

The passphrase derives an AES-256-GCM encryption key using Argon2id with a random salt. ASP.NET Core Data Protection wraps each ciphertext with a key-specific purpose. Keep the database and Data Protection key ring persistent and private. Back up both and store each signing passphrase in the company password manager. Decrypted OpenPGP material exists during signing in process memory and a temporary Linux shared-memory directory.

Serve shared deployments over HTTPS. Close local self-registration if using a restricted employee deployment. With OIDC, group changes are synchronized to the application's local roles when the user signs in.

## How to contribute

Build and test the solution before opening a merge request. Changes to the data model require migrations for both SQLite and MySQL.

## License

ESRP Sign is distributed under the [MIT License](./LICENSE).
