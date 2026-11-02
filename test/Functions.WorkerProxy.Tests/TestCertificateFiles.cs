// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Azure.Functions.Tests;

internal sealed class TestCertificateFiles : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"workerproxy-certificate-{Guid.NewGuid():N}");

    public TestCertificateFiles(DateTimeOffset? expires = null)
    {
        Directory.CreateDirectory(_directory);
        CertificatePath = Path.Combine(_directory, "server.crt");
        KeyPath = Path.Combine(_directory, "server.key");
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=test-worker-proxy", key, HashAlgorithmName.SHA256);
        DateTimeOffset end = expires ?? DateTimeOffset.UtcNow.AddDays(1);
        Certificate = request.CreateSelfSigned(end.AddDays(-2), end);
        File.WriteAllText(CertificatePath, Certificate.ExportCertificatePem());
        File.WriteAllText(KeyPath, key.ExportPkcs8PrivateKeyPem());
        if (OperatingSystem.IsWindows())
        {
            using X509Certificate2 ephemeral = Certificate;
            byte[] pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);
            try
            {
                Certificate = X509CertificateLoader.LoadPkcs12(pkcs12, password: null);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs12);
            }
        }
    }

    public X509Certificate2 Certificate { get; }

    public string CertificatePath { get; }

    public string KeyPath { get; }

    public string[] Arguments =>
        ["--WorkerProxy:CertificatePath", CertificatePath, "--WorkerProxy:CertificateKeyPath", KeyPath];

    public void Dispose()
    {
        Certificate.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
