// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description.DotNet;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Build.Construction;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Versioning;
using static Microsoft.Azure.WebJobs.Script.ScriptConstants;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides NuGet package management functionality.
    /// </summary>
    internal sealed class PackageManager
    {
        private readonly string _functionDirectory;
        private readonly ILogger _logger;

        public PackageManager(string workingDirectory, ILogger logger)
        {
            _functionDirectory = workingDirectory;
            _logger = logger;
        }

        public Task<PackageRestoreResult> RestorePackagesAsync()
        {
            var tcs = new TaskCompletionSource<PackageRestoreResult>();

            string projectPath = null;
            string nugetHome = null;
            string nugetFilePath = null;
            string currentLockFileHash = null;
            try
            {
                projectPath = Path.Combine(_functionDirectory, DotNetConstants.ProjectFileName);
                nugetHome = GetNugetPackagesPath();
                nugetFilePath = ResolveNuGetPath();
                currentLockFileHash = GetCurrentLockFileHash(_functionDirectory);

                // Copy the file to a temporary location, which is where we'll be performing our restore from:
                string tempRestoreLocation = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                string restoreProjectPath = Path.Combine(tempRestoreLocation, Path.GetFileName(projectPath));
                Directory.CreateDirectory(tempRestoreLocation);
                File.Copy(projectPath, restoreProjectPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = nugetFilePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = _functionDirectory,
                    Arguments = string.Format(CultureInfo.InvariantCulture, "restore \"{0}\" --packages \"{1}\"", restoreProjectPath, nugetHome)
                };

                startInfo.Environment.Add(EnvironmentSettingNames.DotnetSkipFirstTimeExperience, "true");

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += ProcessDataReceived;
                process.OutputDataReceived += ProcessDataReceived;
                process.EnableRaisingEvents = true;

                process.Exited += (s, e) =>
                {
                    string lockFileLocation = Path.Combine(tempRestoreLocation, "obj", DotNetConstants.ProjectLockFileName);
                    if (process.ExitCode == 0 && File.Exists(lockFileLocation))
                    {
                        File.Copy(lockFileLocation, Path.Combine(_functionDirectory, DotNetConstants.ProjectLockFileName), true);
                    }

                    string newLockFileHash = GetCurrentLockFileHash(_functionDirectory);
                    var result = new PackageRestoreResult
                    {
                        IsInitialInstall = string.IsNullOrEmpty(currentLockFileHash),
                        ReferencesChanged = !string.Equals(currentLockFileHash, newLockFileHash),
                    };

                    tcs.SetResult(result);
                    process.Close();
                };

                _logger.PackageManagerStartingPackagesRestore();

                process.Start();

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                _logger.PackageManagerRestoreFailed(exc, _functionDirectory, projectPath, nugetHome, nugetFilePath, currentLockFileHash);

                tcs.SetException(exc);
            }

            return tcs.Task;
        }

        internal static string GetCurrentLockFileHash(string functionDirectory)
        {
            string lockFilePath = Path.Combine(functionDirectory, DotNetConstants.ProjectLockFileName);

            if (!File.Exists(lockFilePath))
            {
                return string.Empty;
            }

            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(lockFilePath))
                {
                    byte[] hash = md5.ComputeHash(stream);

                    return hash
                        .Aggregate(new StringBuilder(), (a, b) => a.Append(b.ToString("x2")))
                        .ToString();
                }
            }
        }

        public static string ResolveNuGetPath() => DotNetMuxer.MuxerPathOrDefault();

        public static bool RequiresPackageRestore(string functionPath)
        {
            string projectFilePath = Path.Combine(functionPath, DotNetConstants.ProjectFileName);

            if (!File.Exists(projectFilePath))
            {
                // If there's no project file, we can just return from here
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

            // This mimics the logic used by Nuget to validate a lock file against a given project file.
            // In order to determine whether we have a match, we:
            //  - Read the project frameworks and their dependencies,
            //      extracting the appropriate version range using the lock file format
            //  - Read the lock file depenency groups
            //  - Ensure that each project dependency matches a dependency in the lock file for the
            //      appropriate group matching the framework (including non-framework specific/project wide dependencies)

            LockFile lockFile = null;
            try
            {
                var reader = new LockFileFormat();
                lockFile = reader.Read(lockFilePath);
            }
            catch (FileFormatException)
            {
                return true;
            }

            var projectDependencies = GetProjectDependencies(projectFilePath)
                .Select(d => d.ToLockFileDependencyGroupString())
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dependencyGroups = lockFile.ProjectFileDependencyGroups
                .Where(d => string.Equals(d.FrameworkName, FrameworkConstants.CommonFrameworks.NetStandard20.DotNetFrameworkName, StringComparison.OrdinalIgnoreCase))
                .Aggregate(new List<string>(), (a, d) =>
                {
                    a.AddRange(d.Dependencies.Where(name => !name.StartsWith("NETStandard.Library", StringComparison.OrdinalIgnoreCase)));
                    return a;
                });

            return !projectDependencies.SequenceEqual(dependencyGroups.OrderBy(d => d, StringComparer.OrdinalIgnoreCase));
        }

        private static IList<LibraryRange> GetProjectDependencies(string projectFilePath)
        {
            using (var reader = XmlTextReader.Create(new StringReader(File.ReadAllText(projectFilePath))))
            {
                var root = ProjectRootElement.Create(reader);

                return root.Items
                   .Where(i => PackageReferenceElementName.Equals(i.ItemType, StringComparison.Ordinal))
                   .Select(i => new LibraryRange
                   {
                       Name = i.Include,
                       VersionRange = VersionRange.Parse(i.Metadata.First(m => PackageReferenceVersionElementName.Equals(m.Name, StringComparison.Ordinal)).Value)
                   })
                   .ToList();
            }
        }

        internal static string GetNugetPackagesPath()
        {
            string nugetHome = null;
            string home = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
            if (!string.IsNullOrEmpty(home))
            {
                // We're hosted in Azure
                // Set the NuGet path to %home%\data\Functions\packages\nuget
                // (i.e. d:\home\data\Functions\packages\nuget)
                nugetHome = Path.Combine(home, "data", "Functions", "packages", "nuget");
            }
            else
            {
                string userProfile = Environment.ExpandEnvironmentVariables("%userprofile%");
                nugetHome = Path.Combine(userProfile, ".nuget", "packages");
            }

            return nugetHome;
        }

        private void ProcessDataReceived(object sender, DataReceivedEventArgs e)
        {
            string message = e.Data ?? string.Empty;
            _logger.PackageManagerProcessDataReceived(message);
        }
    }
}
