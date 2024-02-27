// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Text.Json;
using WorkerHarness.Core.Profiling.DotnetTrace;

namespace WorkerHarness.Core.Profiling
{
    public sealed class ProfilerFactory : IProfilerFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<ProfilerFactory> _logger;
        public ProfilerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<ProfilerFactory>();
        }

        public IProfiler? CreateProfiler()
        {
            var dotnetTraceConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "dotnettrace.json");
            var isDotnetTraceConfigFilePresent = File.Exists(dotnetTraceConfigPath);
            _logger.LogTrace($"Dotnet trace config file present: {isDotnetTraceConfigFilePresent}");
            if (isDotnetTraceConfigFilePresent)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                var fileContent = File.ReadAllText(dotnetTraceConfigPath);
                var config = JsonSerializer.Deserialize<DotnetTraceConfig>(fileContent, options);

                var dotnetTraceProfiler = new DotnetTraceProfiler(config!, _loggerFactory.CreateLogger<DotnetTraceProfiler>());
                return dotnetTraceProfiler;
            }
            
            var perfviewProfilePath = Path.Combine(Directory.GetCurrentDirectory(), "perfview.json");
            var isPerfviewConfigFilePresent = File.Exists(perfviewProfilePath);
            _logger.LogTrace($"Perfview config file present: {isPerfviewConfigFilePresent}");
            if (isPerfviewConfigFilePresent)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                var fileContent = File.ReadAllText(perfviewProfilePath);
                var config = JsonSerializer.Deserialize<PerfviewConfig>(fileContent, options);

                var perfviewProfiler = new PerfviewProfiler(config!, _loggerFactory.CreateLogger<PerfviewProfiler>());
                return perfviewProfiler;
            }

            return default;
        }
    }
}
