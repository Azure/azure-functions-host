// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell
{
    internal class PowerShellFileProvisioner : IFuncAppFileProvisioner
    {
        private const string AzModuleName = "Az";
        private const string PowerShellGalleryFindPackagesByIdUri = "https://www.powershellgallery.com/api/v2/FindPackagesById()?id=";

        private const string ProfilePs1FileName = "profile.ps1";
        private const string RequirementsPsd1FileName = "requirements.psd1";

        private const string RequirementsPsd1ResourceFileName = "Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell.requirements.psd1";
        private const string ProfilePs1ResourceFileName = "Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell.profile.ps1";

        private readonly ILogger _logger;

        public PowerShellFileProvisioner(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<FuncAppFileProvisionerFactory>();
        }

        /// <summary>
        /// Adds the required files to the function app
        /// </summary>
        /// <param name="scriptRootPath">The root path of the function app</param>
        /// <returns>An empty completed task <see cref="Task"/></returns>
        public Task ProvisionFiles(string scriptRootPath)
        {
            if (string.IsNullOrWhiteSpace(scriptRootPath))
            {
                throw new ArgumentException("The parameter {0} cannot be null or empty", nameof(scriptRootPath));
            }

            AddRequirementsFile(scriptRootPath);
            AddProfileFile(scriptRootPath);
            return Task.CompletedTask;
        }

        private void AddRequirementsFile(string scriptRootPath)
        {
            string requirementsFilePath = Path.Combine(scriptRootPath, RequirementsPsd1FileName);

            if (!File.Exists(requirementsFilePath))
            {
                _logger.LogDebug($"Creating {RequirementsPsd1FileName} at {scriptRootPath}");

                string requirementsContent = FileUtility.ReadResourceString(RequirementsPsd1ResourceFileName);
                string guidance = null;

                try
                {
                    string majorVersion = GetLatestAzModuleMajorVersion();

                    requirementsContent = Regex.Replace(requirementsContent, @"#(\s?)'Az'", "'Az'");
                    requirementsContent = Regex.Replace(requirementsContent, "MAJOR_VERSION", majorVersion);
                }
                catch
                {
                    guidance = "Uncomment the next line and replace the MAJOR_VERSION, e.g., 'Az' = '2.*'";
                    _logger.LogDebug($"Failed to get Az module version. Edit the {RequirementsPsd1FileName} file when the powershellgallery.com is accessible.");
                }

                requirementsContent = Regex.Replace(requirementsContent, "GUIDANCE", guidance ?? string.Empty);
                File.WriteAllText(requirementsFilePath, requirementsContent);

                _logger.LogDebug($"{RequirementsPsd1FileName} created sucessfully.");
            }
        }

        private void AddProfileFile(string scriptRootPath)
        {
            string profileFilePath = Path.Combine(scriptRootPath, ProfilePs1FileName);

            if (!File.Exists(profileFilePath))
            {
                _logger.LogDebug($"Creating {ProfilePs1FileName} at {scriptRootPath}");

                string content = FileUtility.ReadResourceString(ProfilePs1ResourceFileName);
                File.WriteAllText(profileFilePath, content);

                _logger.LogDebug($"{ProfilePs1FileName} created sucessfully.");
            }
        }

        protected virtual string GetLatestAzModuleMajorVersion()
        {
            Uri address = new Uri($"{PowerShellGalleryFindPackagesByIdUri}'{AzModuleName}'");

            Stream stream = null;
            bool throwException = false;
            string latestMajorVersion = null;

            var retryCount = 3;
            while (true)
            {
                using (var client = new HttpClient())
                {
                    try
                    {
                        var response = client.GetAsync(address).Result;

                        // Throw if not a successful request
                        response.EnsureSuccessStatusCode();

                        stream = response.Content.ReadAsStreamAsync().Result;
                        break;
                    }
                    catch (Exception)
                    {
                        if (retryCount <= 0)
                        {
                            throw;
                        }

                        retryCount--;
                    }
                }
            }

            if (stream == null)
            {
                throwException = true;
            }
            else
            {
                latestMajorVersion = GetModuleMajorVersion(stream);
            }

            // If we could not find the latest module version, error out.
            if (throwException || string.IsNullOrEmpty(latestMajorVersion))
            {
                throw new Exception($@"Failed to get module version for {AzModuleName}.");
            }

            return latestMajorVersion;
        }

        protected internal string GetModuleMajorVersion(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            // Load up the XML response
            XmlDocument doc = new XmlDocument();
            using (XmlReader reader = XmlReader.Create(stream))
            {
                doc.Load(reader);
            }

            const string AtomPrefix = "ps";
            const string AtomUri = "http://www.w3.org/2005/Atom";
            const string DataServicePrefix = "d";
            const string DataServiceUri = "http://schemas.microsoft.com/ado/2007/08/dataservices";
            const string MetadaPrefix = "m";
            const string MetadataUri = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
            const string XPathExpression = "//m:properties[d:IsPrerelease = \"false\"]/d:Version";

            // Add the namespaces for the gallery xml content
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace(AtomPrefix, AtomUri);
            nsmgr.AddNamespace(DataServicePrefix, DataServiceUri);
            nsmgr.AddNamespace(MetadaPrefix, MetadataUri);

            // Find the version information
            XmlNode root = doc.DocumentElement;
            var props = root.SelectNodes(XPathExpression, nsmgr);

            Version latestVersion = null;

            if (props != null && props.Count > 0)
            {
                foreach (XmlNode prop in props)
                {
                    Version.TryParse(prop.FirstChild.Value, out var currentVersion);

                    if (latestVersion == null || currentVersion > latestVersion)
                    {
                        latestVersion = currentVersion;
                    }
                }
            }

            return latestVersion?.ToString().Split('.')[0];
        }
    }
}
