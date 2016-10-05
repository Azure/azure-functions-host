// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides NuGet package management functionality.
    /// </summary>
    internal sealed class PackageManager
    {
        private const string NugetPathEnvironmentKey = "AzureWebJobs_NuGetPath";
        private const string NuGetFileName = "nuget.exe";

        private readonly FunctionMetadata _functionMetadata;
        private readonly TraceWriter _traceWriter;

        public PackageManager(FunctionMetadata metadata, TraceWriter traceWriter)
        {
            _functionMetadata = metadata;
            _traceWriter = traceWriter;
        }

        public Task RestorePackagesAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                string functionDirectory = Path.GetDirectoryName(_functionMetadata.ScriptFile);
                string projectPath = Path.Combine(functionDirectory, DotNetConstants.ProjectFileName);
                string nugetHome = GetNugetPackagesPath();

                var startInfo = new ProcessStartInfo
                {
                    FileName = ResolveNuGetPath(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = functionDirectory,
                    Arguments = string.Format(CultureInfo.InvariantCulture, "restore \"{0}\" -PackagesDirectory \"{1}\"", projectPath, nugetHome)
                };

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += ProcessDataReceived;
                process.OutputDataReceived += ProcessDataReceived;
                process.EnableRaisingEvents = true;

                process.Exited += (s, e) =>
                {
                    tcs.SetResult(process.ExitCode == 0);
                    process.Close();
                };

                _traceWriter.Info("Starting NuGet restore");

                process.Start();

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                tcs.SetException(exc);
            }

            return tcs.Task;
        }

        public static string ResolveNuGetPath()
        {
            // Check if we have the path in the well known environment variable
            string path = Environment.GetEnvironmentVariable(NugetPathEnvironmentKey);

            //// If we don't have the path, try to get a fully qualified path to Kudu's NuGet copy.
            if (string.IsNullOrEmpty(path))
            {
                // Get the latest Kudu extension path
                string kuduFolder = Environment.ExpandEnvironmentVariables("%programfiles(x86)%\\siteextensions\\kudu");
                string kuduPath =
                    Directory.Exists(kuduFolder) ?
                        Directory.GetDirectories(kuduFolder)
                                 .OrderByDescending(d => d)
                                 .FirstOrDefault() :
                        null;

                if (!string.IsNullOrEmpty(kuduPath))
                {
                    path = Path.Combine(kuduPath, "bin\\scripts", NuGetFileName);
                }
            }

            // Return the resolved value or expect NuGet.exe to be present in the path.
            return path ?? NuGetFileName;
        }

        public static bool RequiresPackageRestore(string functionPath)
        {
            string projectFilePath = Path.Combine(functionPath, DotNetConstants.ProjectFileName);

            if (!File.Exists(projectFilePath))
            {
                // If there's no project.json, we can just return from here 
                // as there's nothing to restore
                return false;
            }

            string lockFilePath = Path.Combine(functionPath, DotNetConstants.ProjectLockFileName);

            if (!File.Exists(lockFilePath))
            {
                // If have a project.json and no lock file, we need to 
                // restore the packages, just return true and skip validation
                return true;
            }

            // This mimics the logic used by Nuget to validate a lock file against a given project.json file.
            // In order to determine whether we have a match, we:
            //  - Read the project frameworks and their dependencies, 
            //      extracting the appropriate version range using the lock file format
            //  - Read the lock file depenency groups
            //  - Ensure that each project dependency matches a dependency in the lock file for the
            //      appropriate group matching the framework (including non-framework specific/project wide dependencies)

            var projectFrameworks = GetProjectDependencies(projectFilePath);
            var dependencyGroups = GetDependencyGroups(lockFilePath);

            foreach (var dependencyGroup in dependencyGroups)
            {
                IOrderedEnumerable<string> projectDependencies;
                projectDependencies = projectFrameworks
                    .FirstOrDefault(f => Equals(dependencyGroup.Framework, f.Framework))
                    ?.Dependencies?.OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

                if (!projectDependencies?.SequenceEqual(dependencyGroup.Dependencies.OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) ?? false)
                {
                    return true;
                }
            }

            return false;
        }

        private static IList<FrameworkInfo> GetProjectDependencies(string projectFilePath)
        {
            var frameworks = new List<FrameworkInfo>();

            var jobject = JObject.Parse(File.ReadAllText(projectFilePath));
            var targetFrameworks = jobject.Value<JToken>("frameworks") as JObject;
            var projectDependenciesToken = jobject.Value<JToken>("dependencies") as JObject;

            if (projectDependenciesToken != null)
            {
                IList<string> dependencies = ReadDependencies(projectDependenciesToken);
                frameworks.Add(new FrameworkInfo(null, dependencies));
            }

            if (targetFrameworks != null)
            {
                foreach (var item in targetFrameworks)
                {
                    var dependenciesToken = item.Value.SelectToken("dependencies") as JObject;

                    var framework = NuGetFramework.Parse(item.Key);
                    IList<string> dependencies = ReadDependencies(dependenciesToken);

                    frameworks.Add(new FrameworkInfo(framework, dependencies));
                }
            }

            return frameworks;
        }

        private static IList<string> ReadDependencies(JObject dependenciesToken)
        {
            var dependencies = new List<string>();

            if (dependenciesToken != null)
            {
                foreach (var dependency in dependenciesToken)
                {
                    string name = dependency.Key;
                    string version = null;

                    if (dependency.Value.Type == JTokenType.Object)
                    {
                        // { "PackageName" : { "version" :"1.0" ... }
                        version = dependency.Value.Value<string>("version");
                    }
                    else if (dependency.Value.Type == JTokenType.String)
                    {
                        // { "PackageName" : "1.0" }
                        version = dependency.Value.Value<string>();
                    }
                    else
                    {
                        throw new FormatException($"Unable to parse project.json file. Dependency '{name}' is not correctly formatted.");
                    }

                    var libraryRange = new LibraryRange
                    {
                        Name = name,
                        VersionRange = VersionRange.Parse(version)
                    };

                    dependencies.Add(libraryRange.ToLockFileDependencyGroupString());
                }
            }

            return dependencies;
        }

        private static IList<FrameworkInfo> GetDependencyGroups(string projectLockFilePath)
        {
            var dependencyGroups = new List<FrameworkInfo>();

            var jobject = JObject.Parse(File.ReadAllText(projectLockFilePath));
            var targetFrameworks = jobject.Value<JToken>("projectFileDependencyGroups") as JObject;

            foreach (var dependencyGroup in targetFrameworks)
            {
                NuGetFramework framework = null;
                if (!string.IsNullOrEmpty(dependencyGroup.Key))
                {
                    framework = NuGetFramework.Parse(dependencyGroup.Key);
                }

                IList<string> dependencies = dependencyGroup.Value.ToObject<List<string>>();
                var frameworkInfo = new FrameworkInfo(framework, dependencies);

                dependencyGroups.Add(frameworkInfo);
            }

            return dependencyGroups;
        }

        internal static string GetNugetPackagesPath()
        {
            string nugetHome = null;
            string home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            if (!string.IsNullOrEmpty(home))
            {
                // We're hosted in Azure
                // Set the NuGet path to %home%\data\Functions\packages\nuget
                // (i.e. d:\home\data\Functions\packages\nuget)
                nugetHome = Path.Combine(home, "data\\Functions\\packages\\nuget");
            }
            else
            {
                string userProfile = Environment.ExpandEnvironmentVariables("%userprofile%");
                nugetHome = Path.Combine(userProfile, ".nuget\\packages");
            }

            return nugetHome;
        }

        private void ProcessDataReceived(object sender, DataReceivedEventArgs e)
        {
            _traceWriter.Info(e.Data ?? string.Empty);
        }

        private class FrameworkInfo
        {
            public FrameworkInfo(NuGetFramework framework, IList<string> dependencies)
            {
                Framework = framework;
                Dependencies = new ReadOnlyCollection<string>(dependencies);
            }

            public ReadOnlyCollection<string> Dependencies { get; }

            public NuGetFramework Framework { get; }
        }
    }
}
