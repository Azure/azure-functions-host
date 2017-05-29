// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class JavaScriptCompilationServiceFactory : ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata>
    {
        private const string TypeScriptToolName = "tsc.exe";
        private static readonly ImmutableArray<ScriptType> SupportedScriptTypes = new[] { ScriptType.Javascript, ScriptType.TypeScript }.ToImmutableArray();
        private readonly ScriptHost _host;
        private readonly Lazy<TypeScriptCompilationOptions> _typeScriptOptions;

        public JavaScriptCompilationServiceFactory(ScriptHost host)
        {
            _host = host;
            _typeScriptOptions = new Lazy<TypeScriptCompilationOptions>(CreateTypeScriptCompilationOptions);
        }

        ImmutableArray<ScriptType> ICompilationServiceFactory<ICompilationService<IJavaScriptCompilation>, FunctionMetadata>.SupportedScriptTypes => SupportedScriptTypes;

        public ICompilationService<IJavaScriptCompilation> CreateService(ScriptType scriptType, FunctionMetadata metadata)
        {
            switch (scriptType)
            {
                case ScriptType.Javascript:
                    return new RawJavaScriptCompilationService(metadata);
                case ScriptType.TypeScript:
                    return new TypeScriptCompilationService(_typeScriptOptions.Value);
                default:
                    throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                        "The script type {0} is not supported by the {1}", scriptType, typeof(JavaScriptCompilationServiceFactory).Name));
            }
        }

        private TypeScriptCompilationOptions CreateTypeScriptCompilationOptions()
        {
            return new TypeScriptCompilationOptions
            {
                ToolPath = GetTypeScriptToolPath(),
                OutDir = ".output",
                RootDir = _host.ScriptConfig.RootScriptPath
            };
        }

        private static string GetTypeScriptToolPath()
        {
            string path = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.TypeScriptCompilerPath);
            if (path == null)
            {
                string basePath = Path.Combine(Environment.ExpandEnvironmentVariables("%programfiles(x86)%"), "Microsoft SDKs\\TypeScript");
                if (Directory.Exists(basePath))
                {
                    path = Directory.GetDirectories(basePath)
                        .OrderByDescending(d => d)
                        .FirstOrDefault() ?? string.Empty;

                    path = Path.Combine(path, TypeScriptToolName);
                }
            }

            return path ?? TypeScriptToolName;
        }
    }
}
