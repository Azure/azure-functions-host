// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class MetricsController
    {
        private readonly IHostMetricsProvider _metricsProvider;
        private readonly IFileSystem _fileSystem;
        private readonly FlexConsumptionMetricsPublisherOptions _options;

        public MetricsController(IHostMetricsProvider metricsProvider, IFileSystem fileSystem, IOptions<FlexConsumptionMetricsPublisherOptions> options)
        {
            _metricsProvider = metricsProvider;
            _fileSystem = fileSystem;
            _options = options.Value;
        }

        [HttpGet]
        [Route("admin/metrics/publish1")]
        public async Task<string> EmitMetric([FromQuery] int appFailureCount, [FromQuery] int activeInvocationCount, [FromQuery] int startedInvocationCount, [FromQuery] string functionGroup, [FromQuery] bool isAlwaysReady)
        {
            var stringBuilder = new StringBuilder();

            try
            {
                FlexConsumptionMetricsPublisher.Metrics metrics = new FlexConsumptionMetricsPublisher.Metrics()
                {
                    ActiveInvocationCount = activeInvocationCount,
                    AppFailureCount = appFailureCount,
                    FunctionGroup = functionGroup,
                    IsAlwaysReady = isAlwaysReady,
                    StartedInvocationCount = startedInvocationCount,
                    InstanceId = _metricsProvider.InstanceId
                };

                var metricsContent = JsonConvert.SerializeObject(metrics);
                stringBuilder.AppendLine($"MetricsContent = {metricsContent}");

                var fileName = $"{Guid.NewGuid().ToString().ToLower()}.json";
                string filePath = Path.Combine(_options.MetricsFilePath, fileName);
                stringBuilder.AppendLine($"fileName = {fileName}");

                using (var streamWriter = _fileSystem.File.CreateText(filePath))
                {
                    await streamWriter.WriteAsync(metricsContent);
                }

                stringBuilder.AppendLine("Success");
            }
            catch (Exception e)
            {
                stringBuilder.AppendLine($"$Exception= {e}");
            }
            return stringBuilder.ToString();
        }
    }
}
