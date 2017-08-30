// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description.DotNet;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public class ExtensionsManager : IExtensionsManager
    {
        private readonly string _scriptRootPath;

        public ExtensionsManager(string scriptRootPath)
        {
            _scriptRootPath = scriptRootPath;
        }

        internal string ProjectPath => Path.Combine(_scriptRootPath, "extensions.csproj");


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

                await SaveAndProcessProjectUpdate(project);
            });
        }

        private async Task SaveAndProcessProjectUpdate(ProjectRootElement project)
        {
            project.Save();
            await ProcessExtensionsProject();

            // TODO: We'll need to update a sentinel file in order to notify other workers...
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

                await SaveAndProcessProjectUpdate(project);
            });
        }

        public async Task<IList<ExtensionPackageReference>> GetExtensions()
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

        internal virtual Task ProcessExtensionsProject()
        {
            string dotnetPath = DotNetMuxer.MuxerPathOrDefault();

            var tcs = new TaskCompletionSource<object>();

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
                    WorkingDirectory = _scriptRootPath,
                    Arguments = $"build \"{ExtensionsProjectFileName}\" -o bin"
                };

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += (s, e) => { /*log */};
                process.OutputDataReceived += (s, e) => { /*log */};
                process.EnableRaisingEvents = true;

                process.Exited += (s, e) =>
                {
                    tcs.SetResult(null);
                    process.Close();
                };

                process.Start();

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                // Trace errors...
                tcs.SetException(exc);
            }

            return tcs.Task;

        }

        private ProjectRootElement GetOrCreateProject(string path)
        {
            var root = ProjectRootElement.TryOpen(path);

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
                .AddMetadata(PackageReferenceVersionElementName, "1.0.0-beta1000032", true);

            return root;
        }
    }

    internal static class ProjectExtensions
    {
        public static void AddPackageReference(this ProjectRootElement project, string packageId, string version)
        {
            ProjectItemElement existingPackageReference = project.Items
                .FirstOrDefault(item => item.ItemType == PackageReferenceElementName && item.Include == packageId);

            if (existingPackageReference != null)
            {
                // If the package is already present, move on...
                if (existingPackageReference.Metadata.Any(m => m.Name == PackageReferenceVersionElementName && m.Value == version))
                {
                    return;
                }

                existingPackageReference.Parent.RemoveChild(existingPackageReference);
            }

            ProjectItemGroupElement group = GetUniformItemGroupOrNew(project, PackageReferenceElementName);

            group.AddItem(PackageReferenceElementName, packageId)
                 .AddMetadata(PackageReferenceVersionElementName, version, true);
        }


        public static void RemovePackageReference(this ProjectRootElement project, string packageId)
        {
            ProjectItemElement existingPackageReference = project.Items
                .FirstOrDefault(item => item.ItemType == PackageReferenceElementName && item.Include == packageId);

            if (existingPackageReference != null)
            {
                existingPackageReference.Parent.RemoveChild(existingPackageReference);
            }
        }

        public static ProjectItemGroupElement GetUniformItemGroupOrNew(this ProjectRootElement project, string itemName)
        {
            ProjectItemGroupElement group = project.ItemGroupsReversed.FirstOrDefault(g => g.Items.All(i => i.ItemType == itemName));

            return group ?? project.AddItemGroup();
        }
    }
}
