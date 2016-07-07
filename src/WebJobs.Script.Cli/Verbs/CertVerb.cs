// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Colors.Net;
using Ignite.SharpNetSH;
using NCli;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Helpers;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Automate netsh http to setup SSL and UrlAcls", ShowInHelp = false)]
    internal class CertVerb : BaseVerb
    {
        [Option('p', "port", DefaultValue = 6061, HelpText = "Local port to listen on")]
        public int Port { get; set; }

        [Option('c', "cert", HelpText = "Path for the cert to use. If not supecified, will auto-generate a cert")]
        public string CertPath { get; set; }

        [Option('k', "skipCert", DefaultValue = false, HelpText = "Skip cert/https setup, configures urlacl for http://+:{Port}")]
        public bool SkipCert { get; set; }

        public override Task RunAsync()
        {
            if (!SecurityHelpers.IsAdministrator())
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("When using the cert command you have to run as admin"));

                Environment.Exit(ExitCodes.MustRunAsAdmin);
            }

            if (!SkipCert)
            {
                SetupCerts();
            }
            else
            {
                SetupUrlAcl();
            }

            return Task.CompletedTask;
        }

        private void SetupUrlAcl()
        {
            if (!(NetSH.CMD.Http.Show.UrlAcl($"http://+:{Port}/")?.ResponseObject?.Count > 0))
            {
                NetSH.CMD.Http.Add.UrlAcl($"http://+:{Port}/", Environment.UserName, null);
            }
        }

        private void SetupCerts()
        {
            X509Certificate2 cert = null;
            try
            {
                if (!string.IsNullOrEmpty(CertPath))
                {
                    ColoredConsole.Write($"Please enter '{Path.GetFileName(CertPath)}' password: ");
                    var password = SecurityHelpers.ReadPassword();
                    cert = new X509Certificate2(CertPath, password);
                    ColoredConsole
                        .WriteLine($"{TitleColor($"Certificate:")} {Path.GetFileName(CertPath)}")
                        .WriteLine($"{TitleColor("Subject:")} {cert.Subject}")
                        .WriteLine($"{TitleColor($"Thumbprint:")} {cert.Thumbprint}");
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
            finally
            {
                if (cert != null)
                {
                    cert.Dispose();
                }
            }
        }
    }
}
