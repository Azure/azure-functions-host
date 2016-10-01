// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class TypeScriptCompilationService : ICompilationService<IJavaScriptCompilation>
    {
        private const string DefaultToolName = "tsc.exe";
        private static readonly TypeScriptCompilationOptions DefaultCompilationOptions;

        static TypeScriptCompilationService()
        {
            DefaultCompilationOptions = new TypeScriptCompilationOptions
            {
                ToolPath = GetToolPath(),
                OutDir = ".output"
            };
        }

        public string Language => "TypeScript";

        public IEnumerable<string> SupportedFileTypes => new[] { ".ts" };

        public bool PersistsOutput => true;

        private static string GetToolPath()
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

                    path = Path.Combine(path, DefaultToolName);
                }
            }

            return path ?? DefaultToolName;
        }

        async Task<object> ICompilationService.GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
            => await GetFunctionCompilationAsync(functionMetadata);

        public async Task<IJavaScriptCompilation> GetFunctionCompilationAsync(FunctionMetadata functionMetadata)
        {
            return await TypeScriptCompilation.CompileAsync(functionMetadata.ScriptFile, DefaultCompilationOptions);
        }
    }
}
