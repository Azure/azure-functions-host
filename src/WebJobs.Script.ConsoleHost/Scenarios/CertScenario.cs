// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Ignite.SharpNetSH;
using WebJobs.Script.ConsoleHost.Cli;
using WebJobs.Script.ConsoleHost.Common;
using WebJobs.Script.ConsoleHost.Helpers;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class CertScenario : Scenario
    {
        private readonly CertVerbOptions _options;

        public CertScenario(CertVerbOptions options, ITracer tracer): base(tracer)
        {
            _options = options;
        }

        public override async Task Run()
        {
            if (!SecurityHelpers.IsAdministrator())
            {
                await Tracer.WriteLineAsync("When using the cert command you have to run as admin.");
                Environment.Exit(ExitCodes.MustRunAsAdmin);
            }

            X509Certificate2 cert = null;

            if (!string.IsNullOrEmpty(_options.CertPath))
            {
                await Tracer.WriteAsync($"Please enter '{Path.GetFileName(_options.CertPath)}' password: ");
                var password = SecurityHelpers.ReadPassword();
                cert = await GetUserSuppliedCert(_options.CertPath, password);
            }
            else
            {
                cert = SecurityHelpers.CreateSelfSignedCertificate("localhost");
            }

            new[]
            {
                new X509Store(StoreName.My, StoreLocation.LocalMachine),
                new X509Store(StoreName.Root, StoreLocation.CurrentUser)
            }
            .Where(store => !store.Certificates.Contains(cert))
            .ToList()
            .ForEach(store =>
            {
                store.Open(OpenFlags.MaxAllowed);
                store.Add(cert);
                store.Close();
            });

            if (!(NetSH.CMD.Http.Show.UrlAcl($"https://+:{_options.Port}/")?.ResponseObject?.Count > 0))
            {
                NetSH.CMD.Http.Add.UrlAcl($"https://+:{_options.Port}/", Environment.UserName, null);
            }

            if (!(NetSH.CMD.Http.Show.SSLCert($"0.0.0.0:{_options.Port}")?.ResponseObject?.Count > 0))
            {
                NetSH.CMD.Http.Add.SSLCert(
                    ipPort: $"0.0.0.0:{_options.Port}",
                    certHash: cert.Thumbprint,
                    appId: Assembly.GetExecutingAssembly().GetType().GUID);
            }
        }

        private async Task<X509Certificate2> GetUserSuppliedCert(string path, string password)
        {
            Verify.IsPfxCert(path);
            var cert = new X509Certificate2(path, password);
            await Tracer.WriteLineAsync($"Using user supplied cert {Path.GetFileName(path)}");
            await Tracer.WriteLineAsync($"Subject: {cert.Subject}");
            await Tracer.WriteLineAsync($"Thumpprint: {cert.Thumbprint}");
            return cert;
        }
    }
}
