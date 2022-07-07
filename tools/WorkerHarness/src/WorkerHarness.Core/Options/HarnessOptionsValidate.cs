// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Options
{
    public class HarnessOptionsValidate : IHarnessOptionsValidate
    {
        private readonly ILogger<HarnessOptionsValidate> _logger;

        public HarnessOptionsValidate(ILogger<HarnessOptionsValidate> logger)
        {
            _logger = logger;
        }

        public bool Validate(HarnessOptions harnessOptions)
        {
            bool valid = true;
            string errorMessage = "Invalid or missing --{0} argument";

            if (string.IsNullOrEmpty(harnessOptions.ScenarioFile) || !File.Exists(harnessOptions.ScenarioFile))
            {
                _logger.LogError(errorMessage, "scenarioFile");
                valid = false;
            }

            if (string.IsNullOrEmpty(harnessOptions.WorkerExecutable) || !File.Exists(harnessOptions.WorkerExecutable))
            {
                _logger.LogError(errorMessage, "workerExecutable");
                valid = false;
            }

            if (string.IsNullOrEmpty(harnessOptions.LanguageExecutable) || !File.Exists(harnessOptions.LanguageExecutable))
            {
                _logger.LogError(errorMessage, "languageExecutable");
                valid = false;
            }

            if (string.IsNullOrEmpty(harnessOptions.WorkerDirectory) || !Directory.Exists(harnessOptions.WorkerDirectory))
            {
                _logger.LogError(errorMessage, "workerDirectory");
                valid = false;
            }

            return valid;
        }
    }
}
