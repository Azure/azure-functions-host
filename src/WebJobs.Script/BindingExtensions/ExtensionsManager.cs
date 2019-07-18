// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description.DotNet;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;
using static Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensions
{
    public class ExtensionsManager : IExtensionsManager
    {
        private readonly string _scriptRootPath;
        private readonly ILogger _logger;
        private readonly IExtensionBundleManager _extensionBundleManager;
        private string _nugetFallbackPath;

        public ExtensionsManager(IOptions<ScriptJobHostOptions> hostOptions, ILogger<ExtensionsManager> logger, IExtensionBundleManager extensionBundleManager)
        {
            _scriptRootPath = hostOptions.Value.RootScriptPath;
            _nugetFallbackPath = hostOptions.Value.NugetFallBackPath;
            _logger = logger;
            _extensionBundleManager = extensionBundleManager;
        }

        internal string DefaultExtensionsProjectPath => Path.Combine(_scriptRootPath, ExtensionsProjectFileName);

        private async Task<string> GetBundleProjectPath()
        {
            string bundlePath = await _extensionBundleManager.GetExtensionBundlePath();
            return !string.IsNullOrEmpty(bundlePath) ? Path.Combine(bundlePath, ExtensionsProjectFileName) : null;
        }

        public async Task AddExtensions(params ExtensionPackageReference[] references)
        {
            if (!references.Any())
            {
                return;
            }

            var project = await GetOrCreateProjectAsync(DefaultExtensionsProjectPath);

            // Ensure the metadata generator version we're using is what we expect
            project.AddPackageReference(MetadataGeneratorPackageId, MetadataGeneratorPackageVersion);

            foreach (var extensionReference in references)
            {
                project.AddPackageReference(extensionReference.Id, extensionReference.Version);
            }

            await SaveAndProcessProjectAsync(project);
        }

        private async Task SaveAndProcessProjectAsync(ProjectRootElement project)
        {
            string baseFolder = Path.GetTempPath();

            var tempFolder = Path.Combine(baseFolder, "Functions", "Extensions", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempFolder);

            File.WriteAllText(Path.Combine(tempFolder, ExtensionsProjectFileName), project.RawXml);

            await ProcessExtensionsProject(tempFolder);
        }

        public async Task DeleteExtensions(params string[] extensionIds)
        {
            if (!extensionIds.Any())
            {
                return;
            }

            var project = await GetOrCreateProjectAsync(DefaultExtensionsProjectPath);
            foreach (var id in extensionIds)
            {
                project.RemovePackageReference(id);
            }

            await SaveAndProcessProjectAsync(project);
        }

        public async Task<IEnumerable<ExtensionPackageReference>> GetExtensions()
        {
            string extensionsProjectPath = _extensionBundleManager.IsExtensionBundleConfigured() ? await GetBundleProjectPath() : DefaultExtensionsProjectPath;
            if (string.IsNullOrEmpty(extensionsProjectPath))
            {
                return Enumerable.Empty<ExtensionPackageReference>();
            }

            var project = await GetOrCreateProjectAsync(extensionsProjectPath);

            return project.Items
                .Where(i => PackageReferenceElementName.Equals(i.ItemType, StringComparison.Ordinal) && !MetadataGeneratorPackageId.Equals(i.Include, StringComparison.Ordinal))
                .Select(i => new ExtensionPackageReference
                {
                    Id = i.Include,
                    Version = i.Metadata.FirstOrDefault(m => PackageReferenceVersionElementName.Equals(m.Name, StringComparison.Ordinal))?.Value
                })
                .ToList();
        }

        internal virtual Task ProcessExtensionsProject(string projectFolder)
        {
            string dotnetPath = DotNetMuxer.MuxerPathOrDefault();
            var logBuilder = new StringBuilder();

            var tcs = new TaskCompletionSource<object>();

            _logger.ExtensionsManagerRestoring();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = projectFolder,
                    Arguments = $"build \"{ExtensionsProjectFileName}\" -o bin --force --no-incremental"
                };

                string nugetPath = Path.Combine(Path.GetDirectoryName(DefaultExtensionsProjectPath), "nuget.config");
                if (File.Exists(nugetPath))
                {
                    startInfo.Arguments += $" --configfile \"{nugetPath}\"";
                }

                if (ScriptSettingsManager.Instance.IsAppServiceEnvironment)
                {
                    string nugetCacheLocation = Path.Combine(ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath), ".nuget");
                    startInfo.Arguments += $" --packages \"{nugetCacheLocation}\"";
                }

                SetupProcessEnvironment(startInfo);
                ApplyNugetFallbackFolderConfiguration(startInfo);

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += (s, e) => logBuilder.Append(e.Data);
                process.OutputDataReceived += (s, e) => logBuilder.Append(e.Data);
                process.EnableRaisingEvents = true;

                process.Exited += (s, e) =>
                {
                    int exitCode = process.ExitCode;
                    process.Close();

                    if (exitCode != 0)
                    {
                        tcs.SetException(CreateRestoreException(logBuilder));
                    }
                    else
                    {
                        ProcessResults(projectFolder)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    tcs.SetException(CreateRestoreException(logBuilder, t.Exception));
                                }
                                else
                                {
                                    tcs.SetResult(null);
                                    _logger.ExtensionsManagerRestoreSucceeded();
                                }
                            });
                    }
                };

                process.Start();

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                // Trace errors...
                tcs.SetException(CreateRestoreException(logBuilder, exc));
            }

            return tcs.Task;
        }

        private void SetupProcessEnvironment(ProcessStartInfo startInfo)
        {
            TryAdd(startInfo.Environment, EnvironmentSettingNames.DotnetSkipFirstTimeExperience, "true");
            TryAdd(startInfo.Environment, NugetXmlDocModeSettingName, NugetXmlDocSkipMode);
        }

        private static bool TryAdd(IDictionary<string, string> dictionary, string key, string value)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
                return true;
            }

            return false;
        }

        private void ApplyNugetFallbackFolderConfiguration(ProcessStartInfo startInfo)
        {
            string nugetFallbackFolderRootPath = Path.Combine(Environment.ExpandEnvironmentVariables("%programfiles(x86)%"), NugetFallbackFolderRootName);
            if (string.IsNullOrEmpty(_nugetFallbackPath) && FileUtility.DirectoryExists(nugetFallbackFolderRootPath))
            {
                _nugetFallbackPath = FileUtility.EnumerateDirectories(nugetFallbackFolderRootPath)
                    .Select(directoryPath =>
                    {
                        var directoryName = FileUtility.DirectoryInfoFromDirectoryName(directoryPath).Name;
                        Version.TryParse(directoryName, out Version version);
                        return new Tuple<string, Version>(directoryPath, version);
                    })
                    .Where(p => p.Item2 != null)
                    .OrderByDescending(p => p.Item2)
                    .FirstOrDefault()?.Item1?.ToString();
            }

            if (FileUtility.DirectoryExists(_nugetFallbackPath))
            {
                startInfo.Arguments += $" /p:RestoreFallbackFolders=\"{_nugetFallbackPath}\"";
            }
        }

        private Exception CreateRestoreException(StringBuilder logBuilder, Exception innerException = null)
        {
            return new Exception($"Extension package install failed{Environment.NewLine}{logBuilder.ToString()}", innerException);
        }

        private void LogOutput(string data, StringBuilder logBuilder)
        {
            string message = data ?? string.Empty;
            logBuilder.Append(data);
        }

        private async Task ProcessResults(string tempFolder)
        {
            string sourceBin = Path.Combine(tempFolder, "bin");
            string target = Path.Combine(_scriptRootPath, "bin");

            await FileUtility.DeleteIfExistsAsync(target);

            FileUtility.CopyDirectory(sourceBin, target);

            File.Copy(Path.Combine(tempFolder, ExtensionsProjectFileName), DefaultExtensionsProjectPath, true);
        }

        private Task<ProjectRootElement> GetOrCreateProjectAsync(string path)
        {
            return Task.Run(() =>
            {
                ProjectRootElement root = null;
                if (File.Exists(path))
                {
                    var reader = XmlTextReader.Create(new StringReader(File.ReadAllText(path)));
                    root = ProjectRootElement.Create(reader);
                }

                return root ?? CreateDefaultProject(path);
            });
        }

        private ProjectRootElement CreateDefaultProject(string path)
        {
            var root = ProjectRootElement.Create(path, NewProjectFileOptions.None);
            root.Sdk = "Microsoft.NET.Sdk";

            var propGroup = root.AddPropertyGroup();
            propGroup.AddProperty("TargetFramework", "netstandard2.0");
            propGroup.AddProperty("WarningsAsErrors", string.Empty);

            root.AddItemGroup()
                .AddItem(PackageReferenceElementName, MetadataGeneratorPackageId)
                .AddMetadata(PackageReferenceVersionElementName, MetadataGeneratorPackageVersion, true);

            return root;
        }
    }
}
