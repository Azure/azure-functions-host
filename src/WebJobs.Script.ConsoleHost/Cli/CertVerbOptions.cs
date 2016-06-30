// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using CommandLine;

namespace WebJobs.Script.ConsoleHost.Cli
{
    public class CertVerbOptions : BaseAbstractOptions
    {
        [Option('p', "port", DefaultValue = 6061, HelpText = "Local port to listen on")]
        public int Port { get; set; }

        [Option('c', "cert", HelpText = "Path for the cert to use. If not supecified, will auto-generate a cert.")]
        public string CertPath { get; set; }
    }
}
