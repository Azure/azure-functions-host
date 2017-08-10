// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Azure.WebJobs.Script.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebConfigTests
    {
        private static XmlNamespaceManager _namespace = CreateNamespace();

        [Fact(Skip = "Web.Config")]
        public void TestConfig()
        {
            XDocument config = GetWebConfig();

            AssertNewVersion(config, "System.Net.Http", "4.0.0.0");
            AssertNewVersion(config, "System.IO.Compression", "4.0.0.0");
        }

        private static XmlNamespaceManager CreateNamespace()
        {
            var namespaceManager = new XmlNamespaceManager(new NameTable());
            namespaceManager.AddNamespace("nnn", "urn:schemas-microsoft-com:asm.v1");
            return namespaceManager;
        }

        private static void AssertNewVersion(XDocument config, string assemblyName, string expectedNewVersion)
        {
            var path = $"/configuration/runtime/nnn:assemblyBinding/nnn:dependentAssembly/" +
                $"nnn:assemblyIdentity[@name='{assemblyName}']" +
                $"/following-sibling::nnn:bindingRedirect/" +
                $"@newVersion";

            var name = config.XPathEvaluate("string(" + path + ")", _namespace);
            Assert.Equal(expectedNewVersion, name);
        }

        private static XDocument GetWebConfig()
        {
            string path = "Microsoft.Azure.WebJobs.Script.Tests.WebConfigTests.Web.config";
            using (var stream = Assembly.GetCallingAssembly().GetManifestResourceStream(path))
            {
                return XDocument.Load(stream);
            }
        }
    }
}
