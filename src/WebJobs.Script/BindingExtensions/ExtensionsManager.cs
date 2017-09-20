// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description.DotNet;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensions
{
    public class ExtensionsManager : IExtensionsManager
    {
        private readonly string _scriptRootPath;
        private readonly TraceWriter _traceWriter;
        private readonly ILogger _logger;

        public ExtensionsManager(string scriptRootPath, TraceWriter traceWriter, ILogger logger)
        {
            _scriptRootPath = scriptRootPath;
            _traceWriter = traceWriter;
            _logger = logger;
        }

        internal string ProjectPath => Path.Combine(_scriptRootPath, ExtensionsProjectFileName);

        public async Task AddExtensions(params ExtensionPackageReference[] references)
        {
            if (!references.Any())
            {
                return;
            }

            await Task.Run(async () =>
            {
                var project = GetOrCreateProject(ProjectPath);
                foreach (var extensionReference in references)
                {
                    project.AddPackageReference(extensionReference.Id, extensionReference.Version);
                }
                await SaveAndProcessProjectAsync(project);
            });
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

            await Task.Run(async () =>
            {
                var project = GetOrCreateProject(ProjectPath);
                foreach (var id in extensionIds)
                {
                    project.RemovePackageReference(id);
                }
                await SaveAndProcessProjectAsync(project);
            });
        }

        public async Task<IEnumerable<ExtensionPackageReference>> GetExtensions()
        {
            return await Task.Run(() =>
            {
                var project = GetOrCreateProject(ProjectPath);

                return project.Items
                    .Where(i => PackageReferenceElementName.Equals(i.ItemType, StringComparison.Ordinal) && !ExtensionsPackageId.Equals(i.Include, StringComparison.Ordinal))
                    .Select(i => new ExtensionPackageReference
                    {
                        Id = i.Include,
                        Version = i.Metadata.FirstOrDefault(m => PackageReferenceVersionElementName.Equals(m.Name, StringComparison.Ordinal))?.Value
                    })
                    .ToList();
            });
        }

        internal virtual Task ProcessExtensionsProject(string projectFolder)
        {
            string dotnetPath = DotNetMuxer.MuxerPathOrDefault();
            var logBuilder = new StringBuilder();

            var tcs = new TaskCompletionSource<object>();

            _traceWriter.Info("Restoring extension packages");

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

                string nugetPath = Path.Combine(Path.GetDirectoryName(ProjectPath), "nuget.config");
                if (File.Exists(nugetPath))
                {
                    startInfo.Arguments += $" --configfile \"{nugetPath}\"";
                }

                // If we're running on Azure, on a consumption plan, make sure we cache packages under home
                // to avoid running out of local disk space.
                if (ScriptSettingsManager.Instance.IsAzureEnvironment && ScriptSettingsManager.Instance.IsDynamicSku)
                {
                    string nugetCacheLocation = Path.Combine(ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath), ".nuget");

                    startInfo.Arguments += $" --packages \"{nugetCacheLocation}\"";
                }

                SetupProcessEnvironment(startInfo);

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += (s, e) => logBuilder.Append(e.Data);
                process.OutputDataReceived += (s,e) => logBuilder.Append(e.Data);
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
                        ProcessReults(projectFolder)
                            .ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                {
                                    tcs.SetException(CreateRestoreException(logBuilder, t.Exception));
                                }
                                else
                                {
                                    tcs.SetResult(null);
                                    _traceWriter.Info("Extensions packages restore succeeded.");
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
            foreach (DictionaryEntry environmentVariable in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
            {
                startInfo.Environment.Add(environmentVariable.Key?.ToString(), environmentVariable.Value?.ToString());
            }

            startInfo.EnvironmentVariables.Add("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "true");
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

        private async Task ProcessReults(string tempFolder)
        {
            string sourceBin = Path.Combine(tempFolder, "bin");
            string target = Path.Combine(_scriptRootPath, "bin");

            await FileUtility.DeleteIfExistsAsync(target);

            FileUtility.CopyDirectory(sourceBin, target);

            File.Copy(Path.Combine(tempFolder, ExtensionsProjectFileName), ProjectPath, true);
        }

        private ProjectRootElement GetOrCreateProject(string path)
        {
            ProjectRootElement root = null;
            if (File.Exists(path))
            {
                var reader = XmlTextReader.Create(new StringReader(File.ReadAllText(path)));
                root = ProjectRootElement.Create(reader);
            }
            else
            {
                root = CreateDefaultProject(path);
            }

            return root ?? CreateDefaultProject(path);
        }

        private ProjectRootElement CreateDefaultProject(string path)
        {
            var root = ProjectRootElement.Create(path, NewProjectFileOptions.None);
            root.Sdk = "Microsoft.NET.Sdk";

            root.AddPropertyGroup()
                .AddProperty("TargetFramework", "netstandard2.0");

            root.AddItemGroup()
                .AddItem(PackageReferenceElementName, ExtensionsPackageId)
                .AddMetadata(PackageReferenceVersionElementName, "1.0.0-beta2", true);

            return root;
        }
    }
}
