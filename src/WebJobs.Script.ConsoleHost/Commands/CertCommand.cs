// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Ignite.SharpNetSH;
using WebJobs.Script.ConsoleHost.Common;
using WebJobs.Script.ConsoleHost.Helpers;
using CommandLine;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class CertCommand : Command
    {
        [Option('p', "port", DefaultValue = 6061, HelpText = "Local port to listen on")]
        public int Port { get; set; }

        [Option('c', "cert", HelpText = "Path for the cert to use. If not supecified, will auto-generate a cert.")]
        public string CertPath { get; set; }

        public override async Task Run()
        {
            if (!SecurityHelpers.IsAdministrator())
            {
                TraceInfo("When using the cert command you have to run as admin.");
                Environment.Exit(ExitCodes.MustRunAsAdmin);
            }

            X509Certificate2 cert = null;

            if (!string.IsNullOrEmpty(CertPath))
            {
                TraceInfo($"Please enter '{Path.GetFileName(CertPath)}' password: ");
                var password = SecurityHelpers.ReadPassword();
                cert = GetUserSuppliedCert(CertPath, password);
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

            if (!(NetSH.CMD.Http.Show.UrlAcl($"https://+:{Port}/")?.ResponseObject?.Count > 0))
            {
                NetSH.CMD.Http.Add.UrlAcl($"https://+:{Port}/", Environment.UserName, null);
            }

            if (!(NetSH.CMD.Http.Show.SSLCert($"0.0.0.0:{Port}")?.ResponseObject?.Count > 0))
            {
                NetSH.CMD.Http.Add.SSLCert(
                    ipPort: $"0.0.0.0:{Port}",
                    certHash: cert.Thumbprint,
                    appId: Assembly.GetExecutingAssembly().GetType().GUID);
            }
        }

        private X509Certificate2 GetUserSuppliedCert(string path, string password)
        {
            Verify.IsPfxCert(path);
            var cert = new X509Certificate2(path, password);
            TraceInfo($"Using user supplied cert {Path.GetFileName(path)}");
            TraceInfo($"Subject: {cert.Subject}");
            TraceInfo($"Thumpprint: {cert.Thumbprint}");
            return cert;
        }
    }
}
