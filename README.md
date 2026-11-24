# Aiursoft ESRP Sign

<a name="esrp-sign"></a>

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)
[![Pipeline stat](https://badge-proxy.aiursoft.com/aiursoft/esrpsign/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/esrpsign/-/pipelines)
[![Test Coverage](https://badge-proxy.aiursoft.com/aiursoft/esrpsign/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/esrpsign/-/pipelines)
[![S360 compliance](https://s360.aiursoft.com/badge.svg?project=aiursoft%2Fesrpsign)](https://s360.aiursoft.com/SupplyChain?tab=projects&project=aiursoft%2Fesrpsign&scope=baseline&ecosystem=all)

Aiursoft ESRP Sign is a self-hosted OpenPGP signing service for company releases built on ASP.NET Core (.NET 10).

For local accounts, the default user name is `admin@default.com` and the default password is `Admin@123456!`. Change the password when you first sign in.

## Overview

### What is ESRP Sign?

ESRP Sign creates and protects company OpenPGP signing keys. Employees with permission can create keys or sign with an existing key's passphrase. Anyone can download a public key to verify a release. The web application uses the Aiursoft Template for its UI, authentication, permissions, and database providers.

## CLI & SDK

### SDK

Add the SDK to your .NET project after version `10.0.0` is published by the default-branch release pipeline.

```bash
dotnet add package Aiursoft.EsrpSign.SDK
```

See the [SDK source](src/Aiursoft.EsrpSign.SDK) for the available client methods and models. The client uses AiurProtocol and sends each key creation or signing request once.

```csharp
using Aiursoft.EsrpSign.SDK;
using Aiursoft.EsrpSign.SDK.Models;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddEsrpSignSdk("https://sign.example.com");

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var client = scope.ServiceProvider.GetRequiredService<EsrpSignApiClient>();
var apiKey = Environment.GetEnvironmentVariable("ESRPSIGN_API_KEY")
    ?? throw new InvalidOperationException("Set ESRPSIGN_API_KEY.");
var passphrase = Environment.GetEnvironmentVariable("ESRPSIGN_KEY_PASSPHRASE")
    ?? throw new InvalidOperationException("Set ESRPSIGN_KEY_PASSPHRASE.");
var signingKeyId = Guid.Parse(args[0]);
var result = await client.SignAsync(apiKey, signingKeyId, new SignRequest
{
    Passphrase = passphrase,
    Data = await File.ReadAllBytesAsync("SHA256SUMS")
});
await File.WriteAllBytesAsync("SHA256SUMS.sig", result.Signature);
```

## Install with Docker

Run the installer with the desired port (omit it to choose an available port).

Run the installer from a checkout of this repository. It uses the published image.

```bash
sudo bash install.sh 8080
```

Installation starts after three seconds. Press Ctrl+C to cancel. Then open `http://localhost:8080`.

## Run manually

Requirements about how to run

1. Install [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [Node.js](https://nodejs.org/). On Linux, install the `gnupg` and `libargon2-1` packages for signing.
2. The .NET build automatically runs `npm ci` and `npm run build` to prepare the frontend assets. Node.js and npm must be available when building from source.
3. Execute `dotnet run --project src/Aiursoft.EsrpSign/Aiursoft.EsrpSign.csproj -- --urls http://localhost:5000` to run the app.
4. Use your browser to view [http://localhost:5000](http://localhost:5000).

## Run in Microsoft Visual Studio

1. Install the requirements described above. The build prepares the frontend assets.
2. Open `Aiursoft.EsrpSign.sln` and select `Aiursoft.EsrpSign` as the startup project.
3. Press `F5` to run the app in the Linux or WSL launch environment.

## Run in Docker

First, install Docker [here](https://docs.docker.com/get-docker/).

Run the following commands from the repository root in a Linux shell.

```bash
image=aiursoft/esrpsign
appName=esrpsign
sudo docker build -t "$image" .
sudo docker run -d \
  --name "$appName" --restart unless-stopped -p 127.0.0.1:5000:5000 \
  --mount "type=volume,source=$appName-data,target=/data" "$image"
```

That will start a web server at `http://localhost:5000` and you can test the app.

The docker image has the following context:

| Properties  | Value                           |
|-------------|---------------------------------|
| Image       | aiursoft/esrpsign               |
| Ports       | 5000                            |
| Binary path | /app                            |
| Data path   | /data                           |
| Config path | /data/appsettings.json          |

Keep `/data` persistent for configuration, the SQLite database, uploaded files and Data Protection keys. To use a host directory instead of the named volume, replace the mount argument with `-v /var/www/$appName:/data`.

## Usage

Sign in with the initial local account to configure who can create and use company keys. Open **Company Signing Keys** to access the signing workspace and public key catalog.

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
| `POST /api/v1/signing-keys/{id}/sign-file` | Personal API Key and sign permission | Streamed `multipart/mixed`: UTF-8 passphrase first (`text/plain`), then the original binary file (`application/octet-stream`); returns the same AiurProtocol response |

Use the JSON endpoint for inputs up to 64 KiB, such as `SHA256SUMS`. Browser text uses UTF-8 with LF line endings. For a file up to 64 MiB, use `EsrpSignApiClient.SignFileAsync(apiKey, keyId, passphrase, filePath, cancellationToken)`. Save the returned `Signature` as the detached `.sig` file and verify it against the original file with the corresponding public key.

## Configuration

<a name="configuration-guide"></a>

The defaults run as a standalone application with SQLite, local accounts, local file storage, and no ClickHouse server. Change a setting only after preparing the dependency named in its comment.

The Docker image copies this file to `/data/appsettings.json` on its first start and keeps that copy on later starts. Mount `/data` as a persistent volume. To override a value without editing the file, use an environment variable with double underscores, such as `Storage__Path` or `AppSettings__OIDC__ClientSecret`.

```jsonc
{
  "ConnectionStrings": {
    // Sqlite: use a local database file and no database server. MySql: use an external MySQL server.
    // When changing this value, also replace DefaultConnection with a connection string for that provider.
    "DbType": "Sqlite",
    // With Sqlite, this creates app.db in the process working directory and applies migrations at startup.
    "DefaultConnection": "DataSource=app.db;Cache=Shared"

    // MySql requires a reachable server, database, user, and password. The application applies migrations at startup.
    // sudo docker run -d --name db -e MYSQL_RANDOM_ROOT_PASSWORD=true -e MYSQL_DATABASE=template -e MYSQL_USER=template -e MYSQL_PASSWORD=template_password -p 3306:3306 hub.aiursoft.com/mysql
    //"DbType": "MySql",
    //"DefaultConnection": "Server=localhost;Database=template;Uid=template;Pwd=template_password;"
  },
  "Storage": {
    // Writable local directory for public Workspace files and token-protected private Vault files.
    // This is not an S3 or MinIO bucket.
    // The Docker image changes /tmp/data to /data; mount /data as a persistent volume in production.
    "Path": "/tmp/data",

    // Optional second hostname for this application's own /download endpoints; this is not an S3 or MinIO URL.
    // Example: set https://files.example.com, point that DNS name to this server, and add this Caddy route:
    // files.example.com {
    //     reverse_proxy 127.0.0.1:5000
    // }
    // Replace 127.0.0.1:5000 with the same upstream used by the web UI. Leave empty to use the UI hostname.
    "PublicOrigin": "",

    // False: verified images and configured media can open on the normal web hostname.
    // True: inline viewing works only through PublicOrigin; the same request on the UI hostname downloads the file.
    // True requires PublicOrigin plus DNS, TLS, and a reverse-proxy route to this same application instance.
    "RequireDedicatedInlineOrigin": false,

    // False: only verified images and SafeInlineMediaExtensions open in the browser; every other file downloads.
    // True: every file type may open through PublicOrigin, and HTML or SVG scripts may run there.
    // True requires RequireDedicatedInlineOrigin=true and a PublicOrigin that receives no application cookies.
    "AllowArbitraryInlineOnDedicatedOrigin": false,

    // File extensions allowed to open after content-signature validation. Empty means no extra audio, video, or SVG.
    // Supported values are mp3, mp4, ogg, svg, wav, and webm. This does not grant upload permission.
    "SafeInlineMediaExtensions": ["svg"],

    // SVG opens only when svg is enabled above and its Workspace path starts with an entry in this list.
    // Example: [ "markdown-images" ]. Empty means every SVG downloads.
    "SafeInlineSvgSubfolders": ["project-logo"],

    // These values are copied into newly issued signed upload tokens; existing tokens keep their old policy.
    "UploadPolicy": {
      // False: a valid signed upload token is enough. True: the uploader must also be signed in to this application.
      "RequireAuthenticatedUser": false,

      // False: preserve spaces in uploaded file names. True: save each space as a hyphen.
      "ReplaceSpacesWithHyphens": false
    }
  },
  "Signing": {
    // Empty: use the Template key ring. Keep any alternate path private and persistent.
    "DataProtectionKeyPath": ""
  },
  "AppSettings": {
    // Local: use built-in accounts and passwords. OIDC: redirect sign-in to an external identity provider.
    // OIDC also requires the OIDC section below and a provider callback URL of https://<web-host>/signin-oidc.
    "AuthProvider": "Local",

    // Empty: assign no automatic role. Otherwise, assign this role to every new local or OIDC user.
    // With Local, create the role first. With OIDC, the application creates it during account synchronization.
    "DefaultRole": "",

    // False: sign-in ends when the browser session closes. True: issue a persistent sign-in cookie.
    "PersistsSignIn": false,

    // This section is used only when AuthProvider is OIDC.
    "OIDC": {
      // Provider discovery base URL. It must be reachable by the application and should use HTTPS in production.
      "Authority": "https://auth.aiursoft.com/application/o/esrpsign",
      // Client identifier created for this application in the OIDC provider.
      "ClientId": "",
      // Client secret created by the provider. Set AppSettings__OIDC__ClientSecret instead of committing a real secret.
      "ClientSecret": "",

      // Claim containing role names. On every login, local roles are replaced by these roles plus DefaultRole.
      "RolePropertyName": "groups",
      // Required claim used as the local username when an OIDC account is first created.
      "UsernamePropertyName": "preferred_username",
      // Required claim copied to the local display name on every login.
      "UserDisplayNamePropertyName": "name",
      // Required unique email claim. The provider must keep it verified, unique, and stable.
      "EmailPropertyName": "email",
      // Required immutable account identifier. Keep this mapped to the standard OIDC sub claim.
      "UserIdentityPropertyName": "sub"
    },

    // This section is used only when AuthProvider is Local.
    "Local": {
      // True: show self-registration and allow new local accounts. False: existing users can sign in, but cannot register.
      "AllowRegister": true,
      // True: require only six characters. False: require eight characters with upper, lower, digit, and symbol.
      "AllowWeakPassword": false
    }
  },
  "GlobalSettings": {
    // Leave an entry commented to use the database value or built-in default.
    // Uncommenting an entry forces that value and overrides changes made in the administration UI.

    // True: users may change their display nickname. False: only administrators control it.
    // "Allow_User_Adjust_Nickname": "True",
    // Text shown as the brand name in the footer.
    // "BrandName": "Aiursoft",
    // URL opened when a user clicks the footer brand name.
    // "BrandHomeUrl": "https://www.aiursoft.com/",
    // Mainland China ICP license number. Empty hides it.
    // "Icp": "",
    // Application name shown in the web interface.
    // "ProjectName": "Aiursoft ESRP Sign"
  },
  "Logging": {
    "LogLevel": {
      // Minimum level for application logs. Typical values: Debug, Information, Warning, Error, or None.
      "Default": "Information",
      // Separate minimum level for ASP.NET Core framework logs; Warning reduces routine request noise.
      "Microsoft.AspNetCore": "Warning"
    },
    "Clickhouse": {
      // False: do not send logs to ClickHouse. True: create TableName at startup and send logs in batches.
      // Before enabling, provide a reachable ClickHouse server and valid credentials in ConnectionString.
      "Enabled": false,
      // ClickHouse host, protocol, port, credentials, and database. Used only when Enabled is true.
      "ConnectionString": "Host=localhost;Protocol=http;Port=8123;User=default;Password=;Database=AspNetCoreLogs",
      // ClickHouse table created and used for application logs when Enabled is true.
      "TableName": "EsrpSignLogs"
    }
  },
  // ASP.NET Core Host-header allowlist; this is not a CORS setting.
  // * accepts every hostname. In production, use semicolon-separated names such as example.com;www.example.com.
  "AllowedHosts": "*"
}
```

### Shared settings

- `ConnectionStrings` selects the database provider and connection. Prepare the external database before choosing MySQL; keep the SQLite file persistent when using SQLite.
- `Storage` configures local application files. Keep the storage directory persistent and use the documented hostname and upload policies for shared downloads.
- `AppSettings` controls authentication and account synchronization. Choose local accounts or configure the OIDC authority, client and `/signin-oidc` callback; use environment variables for credentials.
- Uncommented `GlobalSettings` entries override the database-backed administration settings. Leave entries commented to use the stored values or application defaults.
- `Logging:LogLevel` controls application and framework verbosity. Configure the optional ClickHouse exporter only when its server and credentials are ready.
- `AllowedHosts` is the ASP.NET Core Host-header allowlist, not a CORS setting.

ESRP Sign reads current permissions directly from the database.

`Signing:DataProtectionKeyPath` overrides the Data Protection key ring path. The Docker default is `/data/.aspnet/DataProtection-Keys`.

The passphrase derives an AES-256-GCM encryption key using Argon2id with a random salt. ASP.NET Core Data Protection wraps each ciphertext with a key-specific purpose. Keep the database and Data Protection key ring persistent and private. Back up both and store each signing passphrase in the company password manager. Decrypted OpenPGP material exists during signing in process memory and a temporary Linux shared-memory directory.

Serve shared deployments over HTTPS. Close local self-registration if using a restricted employee deployment. With OIDC, group changes are synchronized to the application's local roles when the user signs in.

## Documentation

The [application configuration](src/Aiursoft.EsrpSign/appsettings.json) documents the available settings. The [shared file manifest](ninja.yaml) identifies files maintained across derived applications.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you have push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.

Build and test the solution before opening a merge request. Changes to the data model require migrations for both SQLite and MySQL.

## License

Licensed under the [MIT License](LICENSE).
