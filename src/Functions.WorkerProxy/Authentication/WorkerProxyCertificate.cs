// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Azure.Functions.WorkerProxy.Authentication;

/// <summary>
/// Owns a self-signed certificate and private key for this proxy process only.
/// </summary>
internal sealed class WorkerProxyCertificate : IDisposable
{
    public WorkerProxyCertificate()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=WorkerProxy", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Certificate = request.CreateSelfSigned(now.AddMinutes(-1), now.AddYears(1));
        if (OperatingSystem.IsWindows())
        {
            // Schannel cannot serve a certificate backed by an ephemeral CNG key.
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

    public void Dispose() => Certificate.Dispose();
}
