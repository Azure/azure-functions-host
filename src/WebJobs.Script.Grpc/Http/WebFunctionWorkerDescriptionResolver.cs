// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http;

internal enum WebFunctionLanguage
{
    None,
    Node,
    Python,
    Java
}

internal static class WebFunctionWorkerDescriptionResolver
{
    internal const string EntryPointSetting = "FUNCTIONS_WEB_ENTRYPOINT";
    internal const string PythonProtocolSetting = "FUNCTIONS_WEB_PYTHON_PROTOCOL";

    private static readonly string[] NodeEntryPoints =
        ["index.js", "server.js", "app.js", "index.mjs", "server.mjs", "app.mjs", "index.cjs", "server.cjs", "app.cjs"];

    private static readonly string[] PythonEntryPoints = ["wsgi.py", "app.py", "main.py"];
    private static readonly string[] JavaDirectories = [string.Empty, "target", Path.Combine("build", "libs")];

    internal static HttpWorkerDescription Resolve(
        string rootPath, IConfiguration configuration, int port, out WebFunctionLanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!Directory.Exists(rootPath))
        {
            throw new HostConfigurationException($"The web function application directory '{rootPath}' does not exist.");
        }

        string configuredRuntime = configuration[EnvironmentSettingNames.FunctionWorkerRuntime];
        language = GetConfiguredLanguage(configuredRuntime);
        if (language is WebFunctionLanguage.None)
        {
            if (!string.IsNullOrWhiteSpace(configuredRuntime) &&
                !string.Equals(configuredRuntime, RpcWorkerConstants.CustomHandlerLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                throw new HostConfigurationException(
                    $"Web functions do not support the configured runtime '{configuredRuntime}'. Supported runtimes are node, python, and java.");
            }

            language = DetectLanguage(rootPath);
        }

        string entryPoint = configuration[EntryPointSetting];
        return language switch
        {
            WebFunctionLanguage.Node => ResolveNode(rootPath, entryPoint),
            WebFunctionLanguage.Python => ResolvePython(rootPath, entryPoint, configuration[PythonProtocolSetting], port),
            WebFunctionLanguage.Java => ResolveJava(rootPath, entryPoint, configuration["JAVA_HOME"]),
            _ => throw new HostConfigurationException("Unable to determine the web function runtime.")
        };
    }

    internal static WebFunctionLanguage GetConfiguredLanguage(string runtime)
    {
        if (string.Equals(runtime, RpcWorkerConstants.NodeLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
        {
            return WebFunctionLanguage.Node;
        }

        if (string.Equals(runtime, RpcWorkerConstants.PythonLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
        {
            return WebFunctionLanguage.Python;
        }

        if (string.Equals(runtime, RpcWorkerConstants.JavaLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
        {
            return WebFunctionLanguage.Java;
        }

        return WebFunctionLanguage.None;
    }

    private static WebFunctionLanguage DetectLanguage(string rootPath)
    {
        List<WebFunctionLanguage> candidates = [];
        if (File.Exists(Path.Combine(rootPath, "package.json")) || FindExistingFile(rootPath, NodeEntryPoints) is not null)
        {
            candidates.Add(WebFunctionLanguage.Node);
        }

        if (FindExistingFile(rootPath, PythonEntryPoints) is not null)
        {
            candidates.Add(WebFunctionLanguage.Python);
        }

        if (FindJavaArchives(rootPath).Count > 0)
        {
            candidates.Add(WebFunctionLanguage.Java);
        }

        if (candidates.Count is not 1)
        {
            throw new HostConfigurationException(
                $"Unable to determine a single web function runtime from the application files. Set {EnvironmentSettingNames.FunctionWorkerRuntime} to node, python, or java.");
        }

        return candidates[0];
    }

    private static HttpWorkerDescription ResolveNode(string rootPath, string entryPoint)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            string packagePath = Path.Combine(rootPath, "package.json");
            if (File.Exists(packagePath))
            {
                try
                {
                    using JsonDocument package = JsonDocument.Parse(File.ReadAllText(packagePath));
                    if (package.RootElement.ValueKind is not JsonValueKind.Object)
                    {
                        throw new HostConfigurationException("The web function package.json must contain a JSON object.");
                    }

                    if (package.RootElement.TryGetProperty("scripts", out JsonElement scripts))
                    {
                        if (scripts.ValueKind is not JsonValueKind.Object)
                        {
                            throw new HostConfigurationException("The web function package.json 'scripts' property must be an object.");
                        }

                        if (scripts.TryGetProperty("start", out JsonElement start))
                        {
                            if (start.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(start.GetString()))
                            {
                                throw new HostConfigurationException("The web function package.json 'scripts.start' property must be a non-empty string.");
                            }

                            return new HttpWorkerDescription
                            {
                                DefaultExecutablePath = OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                                Arguments = OperatingSystem.IsWindows()
                                    ? ["/d", "/s", "/c", "npm.cmd", "run", "start"]
                                    : ["run", "start"],
                            };
                        }
                    }

                    if (package.RootElement.TryGetProperty("main", out JsonElement main))
                    {
                        if (main.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(main.GetString()))
                        {
                            throw new HostConfigurationException("The web function package.json 'main' property must be a non-empty string.");
                        }

                        entryPoint = main.GetString();
                    }
                }
                catch (JsonException exception)
                {
                    throw new HostConfigurationException("The web function package.json is not valid JSON.", exception);
                }
            }

            entryPoint ??= FindExistingFile(rootPath, NodeEntryPoints);
        }

        entryPoint = RequireExistingFile(rootPath, entryPoint, "Node.js entry point");
        return new HttpWorkerDescription
        {
            DefaultExecutablePath = "node",
            DefaultWorkerPath = entryPoint,
        };
    }

    private static HttpWorkerDescription ResolvePython(string rootPath, string entryPoint, string protocol, int port)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            string file = FindExistingFile(rootPath, PythonEntryPoints);
            if (file is null)
            {
                throw new HostConfigurationException(
                    $"No Python web application was found. Add wsgi.py, app.py, or main.py, or set {EntryPointSetting} to module:application.");
            }

            entryPoint = $"{Path.GetFileNameWithoutExtension(file)}:app";
        }

        ValidateArgument(entryPoint, "Python entry point");
        int separator = entryPoint.IndexOf(':');
        if (separator <= 0 || separator == entryPoint.Length - 1)
        {
            throw new HostConfigurationException($"The Python {EntryPointSetting} must have the form module:application.");
        }

        string address = $"{WorkerConstants.HostName}:{port.ToString(CultureInfo.InvariantCulture)}";
        List<string> arguments;
        if (string.IsNullOrWhiteSpace(protocol) || string.Equals(protocol, "wsgi", StringComparison.OrdinalIgnoreCase))
        {
            arguments = OperatingSystem.IsWindows()
                ? ["-m", "waitress", $"--listen={address}"]
                : ["-m", "gunicorn", "--bind", address, "--worker-class", "gthread", "--threads", "8"];
        }
        else if (string.Equals(protocol, "asgi", StringComparison.OrdinalIgnoreCase))
        {
            arguments = ["-m", "uvicorn", "--host", WorkerConstants.HostName, "--port", port.ToString(CultureInfo.InvariantCulture)];
        }
        else
        {
            throw new HostConfigurationException($"The {PythonProtocolSetting} setting must be 'wsgi' or 'asgi'.");
        }

        return new HttpWorkerDescription
        {
            DefaultExecutablePath = "python",
            Arguments = arguments,
            DefaultWorkerPath = entryPoint,
            UseStdErrorStreamForErrorsOnly = false,
        };
    }

    private static HttpWorkerDescription ResolveJava(string rootPath, string entryPoint, string javaHome)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            List<string> archives = FindJavaArchives(rootPath);
            if (archives.Count is not 1)
            {
                throw new HostConfigurationException(
                    $"Expected one runnable Java archive in the app root, target, or build/libs. Set {EntryPointSetting} to the archive path.");
            }

            entryPoint = archives[0];
        }

        entryPoint = RequireExistingFile(rootPath, entryPoint, "Java archive");
        string executablePath = "java";
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            executablePath = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (!File.Exists(executablePath))
            {
                throw new HostConfigurationException($"The configured JAVA_HOME '{javaHome}' does not contain a Java executable.");
            }
        }

        return new HttpWorkerDescription
        {
            DefaultExecutablePath = executablePath,
            Arguments = ["-jar"],
            DefaultWorkerPath = entryPoint,
        };
    }

    private static string FindExistingFile(string rootPath, IEnumerable<string> candidates)
    {
        return candidates.FirstOrDefault(candidate => File.Exists(Path.Combine(rootPath, candidate)));
    }

    private static List<string> FindJavaArchives(string rootPath)
    {
        return JavaDirectories
            .Select(directory => Path.Combine(rootPath, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jar"))
            .Where(path =>
                !path.EndsWith("-sources.jar", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("-javadoc.jar", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("-plain.jar", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(rootPath, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static string RequireExistingFile(string rootPath, string entryPoint, string description)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            throw new HostConfigurationException($"{description} was not found. Set {EntryPointSetting} to an application file.");
        }

        ValidateArgument(entryPoint, description);
        string fullPath = Path.IsPathRooted(entryPoint) ? entryPoint : Path.Combine(rootPath, entryPoint);
        if (!File.Exists(fullPath))
        {
            throw new HostConfigurationException($"{description} '{entryPoint}' does not exist.");
        }

        return entryPoint;
    }

    private static void ValidateArgument(string argument, string description)
    {
        if (argument.Contains('"') || argument.Any(char.IsControl))
        {
            throw new HostConfigurationException($"{description} cannot contain double quotes or control characters.");
        }
    }
}
