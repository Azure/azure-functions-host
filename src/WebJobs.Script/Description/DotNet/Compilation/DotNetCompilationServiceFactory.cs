// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class DotNetCompilationServiceFactory : ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>
    {
        private static readonly ImmutableArray<string> SupportedLanguages = new[] { DotNetScriptTypes.CSharp, DotNetScriptTypes.FSharp, DotNetScriptTypes.DotNetAssembly }.ToImmutableArray();
        private static OptimizationLevel? _optimizationLevel;
        private readonly ILoggerFactory _loggerFactory;

        public DotNetCompilationServiceFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        ImmutableArray<string> ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver>.SupportedLanguages => SupportedLanguages;

        internal static OptimizationLevel OptimizationLevel
        {
            get
            {
                if (_optimizationLevel == null)
                {
                    // Get the release mode setting. If set, this will take priority over environment settings.
                    string releaseModeSetting = SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.CompilationReleaseMode);
                    if (!bool.TryParse(releaseModeSetting, out bool releaseMode) &&
                        (SystemEnvironment.Instance.IsAppServiceEnvironment() ||
                        SystemEnvironment.Instance.IsLinuxContainerEnvironment()) &&
                        !SystemEnvironment.Instance.IsRemoteDebuggingEnabled())
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

        public ICompilationService<IDotNetCompilation> CreateService(string language, IFunctionMetadataResolver metadata)
        {
            switch (language)
            {
                case DotNetScriptTypes.CSharp:
                    return new CSharpCompilationService(metadata, OptimizationLevel);
                case DotNetScriptTypes.FSharp:
                    return new FSharpCompilationService(metadata, OptimizationLevel, _loggerFactory);
                case DotNetScriptTypes.DotNetAssembly:
                    return new RawAssemblyCompilationService();
                default:
                    throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                        "The language {0} is not supported by the {1}", language, typeof(DotNetCompilationServiceFactory).Name));
            }
        }
    }
}
