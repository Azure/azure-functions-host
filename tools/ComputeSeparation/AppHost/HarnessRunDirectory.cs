// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Owns an empty script root, logs, and Host-generated file secrets for one local run.
/// </summary>
internal sealed class HarnessRunDirectory : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("functions-aspire-");

    public HarnessRunDirectory()
    {
        ScriptPath = Path.Combine(_directory.FullName, "app");
        LogPath = Path.Combine(_directory.FullName, "logs");
        SecretsPath = Path.Combine(_directory.FullName, "secrets");
        Directory.CreateDirectory(ScriptPath);
        Directory.CreateDirectory(LogPath);
        Directory.CreateDirectory(SecretsPath);
        CertificatePath = Path.Combine(_directory.FullName, "server.crt");
        CertificateKeyPath = Path.Combine(_directory.FullName, "server.key");
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=local-worker-proxy", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(CertificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(CertificateKeyPath, key.ExportPkcs8PrivateKeyPem());
    }

    public string ScriptPath { get; }

    public string LogPath { get; }

    public string SecretsPath { get; }

    public string CertificatePath { get; }

    public string CertificateKeyPath { get; }

    public void Dispose()
    {
        _directory.Delete(recursive: true);
    }
}
