// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [CLSCompliant(false)]
    public sealed class DotNetCompilationServiceFactory : ICompilationServiceFactory
    {
        private static readonly ImmutableArray<ScriptType> SupportedScriptTypes = new[] { ScriptType.CSharp, ScriptType.FSharp, ScriptType.DotNetAssembly }.ToImmutableArray();
        private static OptimizationLevel? _optimizationLevel;

        ImmutableArray<ScriptType> ICompilationServiceFactory.SupportedScriptTypes
        {
            get
            {
                return SupportedScriptTypes;
            }
        }

        internal static OptimizationLevel OptimizationLevel
        {
            get
            {
                if (_optimizationLevel == null)
                {
                    // Get the release mode setting. If set, this will take priority over environment settings.
                    string releaseModeSetting = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.CompilationReleaseMode);
                    bool releaseMode;
                    if (!bool.TryParse(releaseModeSetting, out releaseMode) &&
                        ScriptSettingsManager.Instance.IsAzureEnvironment &&
                        !ScriptSettingsManager.Instance.IsRemoteDebuggingEnabled)
                    {
                        // If the release mode setting is not set, we're running in Azure
                        // and not remote debugging, use release mode.
                        releaseMode = true;
                    }

                    _optimizationLevel = releaseMode ? OptimizationLevel.Release : OptimizationLevel.Debug;
                }

                return _optimizationLevel.Value;
            }
        }

        internal static void SetOptimizationLevel(OptimizationLevel? level)
        {
            _optimizationLevel = level;
        }

        public ICompilationService CreateService(ScriptType scriptType, IFunctionMetadataResolver metadataResolver)
        {
            switch (scriptType)
            {
                case ScriptType.CSharp:
                    return new CSharpCompilationService(metadataResolver, OptimizationLevel);
                case ScriptType.FSharp:
                    return new FSharpCompiler(metadataResolver, OptimizationLevel);
                case ScriptType.DotNetAssembly:
                    return new RawAssemblyCompilationService();
                default:
                    throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                        "The script type {0} is not supported by the {1}", scriptType, typeof(DotNetCompilationServiceFactory).Name));
            }
        }
    }
}
