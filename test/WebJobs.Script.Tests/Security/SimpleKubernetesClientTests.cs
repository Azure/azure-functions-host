// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Moq.Protected;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SimpleKubernetesClientTests : IDisposable
    {
        [Theory]
        [InlineData(HttpStatusCode.OK, "{}", 0)]
        [InlineData(HttpStatusCode.OK, "{'data': {}}", 0)]
        [InlineData(HttpStatusCode.OK, "{'data': {'key': 'dmFsdWU='}}", 1)]
        public async Task Get_From_ApiServer_No_Data(HttpStatusCode statusCode, string content, int length)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsKubernetesSecretName, "test");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHost, "127.0.0.1");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHttpsPort, "443");

            var fullFileSystem = new FileSystem();
            var fileSystem = new Mock<IFileSystem>();
            var fileBase = new Mock<FileBase>();
            var directoryBase = new Mock<DirectoryBase>();

            fileSystem.SetupGet(f => f.Path).Returns(fullFileSystem.Path);
            fileSystem.SetupGet(f => f.File).Returns(fileBase.Object);
            fileSystem.SetupGet(f => f.Directory).Returns(directoryBase.Object);
            fileBase.Setup(f => f.Exists("/run/secrets/kubernetes.io/serviceaccount/namespace")).Returns(true);
            fileBase.Setup(f => f.Exists("/run/secrets/kubernetes.io/serviceaccount/token")).Returns(true);
            fileBase.Setup(f => f.Exists("/run/secrets/kubernetes.io/serviceaccount/ca.crt")).Returns(true);

            fileBase
                .Setup(f => f.Open("/run/secrets/kubernetes.io/serviceaccount/token", It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
                .Returns(() =>
                {
                    var token = new MemoryStream(Encoding.UTF8.GetBytes("test_token"));
                    token.Position = 0;
                    return token;
                });
            fileBase
                .Setup(f => f.Open("/run/secrets/kubernetes.io/serviceaccount/namespace", It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
                .Returns(() =>
                {
                    var ns = new MemoryStream(Encoding.UTF8.GetBytes("namespace"));
                    ns.Position = 0;
                    return ns;
                });

            FileUtility.Instance = fileSystem.Object;

            var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>()).ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,

                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });

            var client = new SimpleKubernetesClient(environment, new HttpClient(handlerMock.Object), loggerFactory.CreateLogger<SimpleKubernetesClient>());
            var secrets = await client.GetSecrets();

            Assert.NotNull(secrets);
            Assert.Equal(secrets.Count, length);
        }

        [Fact]
        public void ValidateCertificate_NoErrors_Accepts()
        {
            using X509Certificate2 ca = CreateCa("Test CA");
            using X509Certificate2 server = CreateCertSignedBy("kubernetes.default.svc", ca);

            Assert.True(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(ca, server, SslPolicyErrors.None));
        }

        [Fact]
        public void ValidateCertificate_NameMismatch_Rejects()
        {
            using X509Certificate2 ca = CreateCa("Test CA");
            using X509Certificate2 server = CreateCertSignedBy("kubernetes.default.svc", ca);

            Assert.False(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(ca, server, SslPolicyErrors.RemoteCertificateNameMismatch));
        }

        [Fact]
        public void ValidateCertificate_NotAvailable_Rejects()
        {
            using X509Certificate2 ca = CreateCa("Test CA");
            using X509Certificate2 server = CreateCertSignedBy("kubernetes.default.svc", ca);

            Assert.False(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(ca, server, SslPolicyErrors.RemoteCertificateNotAvailable));
        }

        [Fact]
        public void ValidateCertificate_NameMismatchPlusChainErrors_Rejects()
        {
            using X509Certificate2 ca = CreateCa("Test CA");
            using X509Certificate2 server = CreateCertSignedBy("kubernetes.default.svc", ca);

            Assert.False(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(
                ca,
                server,
                SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void ValidateCertificate_ChainErrors_ServerSignedByCustomCa_Accepts()
        {
            using X509Certificate2 ca = CreateCa("Test CA");
            using X509Certificate2 server = CreateCertSignedBy("kubernetes.default.svc", ca);

            Assert.True(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(ca, server, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        // Asserts that the validator does not accept a chain rooted at a CA other
        // than the one supplied as the trust anchor.
        [Fact]
        public void ValidateCertificate_ChainErrors_SelfSignedCertWithMatchingName_Rejects()
        {
            using X509Certificate2 ca = CreateCa("Test CA");
            using X509Certificate2 unrelatedSelfSigned = CreateSelfSigned("kubernetes.default.svc");

            Assert.False(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(ca, unrelatedSelfSigned, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void ValidateCertificate_ChainErrors_ServerSignedByDifferentCa_Rejects()
        {
            using X509Certificate2 trustedCa = CreateCa("Trusted CA");
            using X509Certificate2 otherCa = CreateCa("Other CA");
            using X509Certificate2 server = CreateCertSignedBy("kubernetes.default.svc", otherCa);

            Assert.False(SimpleKubernetesClient.ValidateCertificateAgainstCustomCa(trustedCa, server, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        private static X509Certificate2 CreateCa(string commonName)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddDays(1);

            // Persist the private key on the returned cert so it can sign children.
            using X509Certificate2 self = request.CreateSelfSigned(notBefore, notAfter);
            return X509CertificateLoader.LoadPkcs12(self.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
        }

        private static X509Certificate2 CreateCertSignedBy(string subjectAltName, X509Certificate2 caWithKey)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={subjectAltName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(subjectAltName);
            request.CertificateExtensions.Add(sanBuilder.Build());

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddHours(1);

            byte[] serialNumber = RandomNumberGenerator.GetBytes(8);
            using X509Certificate2 signed = request.Create(caWithKey, notBefore, notAfter, serialNumber);
            return signed.CopyWithPrivateKey(rsa);
        }

        private static X509Certificate2 CreateSelfSigned(string subjectAltName)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={subjectAltName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(subjectAltName);
            request.CertificateExtensions.Add(sanBuilder.Build());

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddHours(1);
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        public void Dispose()
        {
            FileUtility.Instance = null;
        }
    }
}