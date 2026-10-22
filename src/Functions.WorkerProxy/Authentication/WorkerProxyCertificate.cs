// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Authentication;

/// <summary>
/// Loads and owns the certificate and private key provisioned before this proxy starts.
/// </summary>
internal sealed class WorkerProxyCertificate : IDisposable
{
    public WorkerProxyCertificate(IOptions<WorkerProxyOptions> options)
    {
        WorkerProxyOptions configured = options.Value;
        Certificate = X509Certificate2.CreateFromPemFile(configured.CertificatePath, configured.CertificateKeyPath);
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
