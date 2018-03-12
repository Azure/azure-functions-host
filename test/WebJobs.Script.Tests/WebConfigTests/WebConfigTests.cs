// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Azure.WebJobs.Extensions.BotFramework;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebConfigTests
    {
        private static XmlNamespaceManager _namespace = CreateNamespace();

        [Fact]
        public void TestConfig()
        {
            XDocument config = GetWebConfig();

            AssertNewVersion(config, "System.Net.Http", "4.0.0.0");
            AssertNewVersion(config, "System.IO.Compression", "4.0.0.0");

            AssertVersionRedirects(config, typeof(QueueAttribute)); // Microsoft.Azure.WebJobs
            AssertVersionRedirects(config, typeof(JobHost)); // Microsoft.Azure.WebJobs.Host
            AssertVersionRedirects(config, typeof(ILogReader)); // Microsoft.Azure.WebJobs.Logging
            AssertVersionRedirects(config, typeof(DefaultTelemetryClientFactory)); // Microsoft.Azure.WebJobs.Logging.ApplicationInsights
            AssertVersionRedirects(config, typeof(ServiceBusAttribute)); // Microsoft.Azure.WebJobs.ServiceBus

            AssertVersionRedirects(config, typeof(TimerTriggerAttribute)); // Microsoft.Azure.WebJobs.Extensions
            AssertVersionRedirects(config, typeof(ApiHubFileAttribute)); // Microsoft.Azure.WebJobs.Extensions.ApiHub
            AssertVersionRedirects(config, typeof(BotAttribute)); // Microsoft.Azure.WebJobs.Extensions.BotFramework
            AssertVersionRedirects(config, typeof(DocumentDBAttribute)); // Microsoft.Azure.WebJobs.Extensions.DocumentDB
            AssertVersionRedirects(config, typeof(EventGridTriggerAttribute)); // Microsoft.Azure.WebJobs.Extensions.EventGrid
            AssertVersionRedirects(config, typeof(HttpTriggerAttribute)); // Microsoft.Azure.WebJobs.Extensions.Http
            AssertVersionRedirects(config, typeof(MobileTableAttribute)); // Microsoft.Azure.WebJobs.MobileApps
            AssertVersionRedirects(config, typeof(NotificationHubAttribute)); // Microsoft.Azure.WebJobs.NotificationHubs
            AssertVersionRedirects(config, typeof(SendGridAttribute)); // Microsoft.Azure.WebJobs.SendGrid
            AssertVersionRedirects(config, typeof(TwilioSmsAttribute)); // Microsoft.Azure.WebJobs.Twilio
        }

        private static XmlNamespaceManager CreateNamespace()
        {
            var namespaceManager = new XmlNamespaceManager(new NameTable());
            namespaceManager.AddNamespace("nnn", "urn:schemas-microsoft-com:asm.v1");
            return namespaceManager;
        }

        private static void AssertVersionRedirects(XDocument config, Type type)
        {
            AssemblyName assemblyDetails = type.Assembly.GetName();
            string assemblyName = assemblyDetails.Name;
            string redirectVersion = assemblyDetails.Version.ToString();

            AssertNewVersion(config, assemblyName, redirectVersion);
            AssertOldVersion(config, assemblyName, redirectVersion);
        }

        private static void AssertNewVersion(XDocument config, string assemblyName, string expectedNewVersion)
        {
            var path = $"/configuration/runtime/nnn:assemblyBinding/nnn:dependentAssembly/" +
                $"nnn:assemblyIdentity[@name='{assemblyName}']" +
                $"/following-sibling::nnn:bindingRedirect/" +
                $"@newVersion";

            string version = config.XPathEvaluate("string(" + path + ")", _namespace).ToString();
            Assert.True(version == expectedNewVersion,
                $"Web.config 'newVersion' does not match for binding redirect '{assemblyName}'. Expected '{expectedNewVersion}'. Actual '{version}'.");
        }

        private static void AssertOldVersion(XDocument config, string assemblyName, string expectedRedirectVersion)
        {
            var path = $"/configuration/runtime/nnn:assemblyBinding/nnn:dependentAssembly/" +
                $"nnn:assemblyIdentity[@name='{assemblyName}']" +
                $"/following-sibling::nnn:bindingRedirect/" +
                $"@oldVersion";

            string version = config.XPathEvaluate("string(" + path + ")", _namespace).ToString();
            string expectedVersion = $"0.0.0.0-{expectedRedirectVersion}";
            Assert.True(version == expectedVersion,
                $"Web.config 'oldVersion' does not match for binding redirect '{assemblyName}'. Expected '{expectedVersion}'. Actual '{version}'.");
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