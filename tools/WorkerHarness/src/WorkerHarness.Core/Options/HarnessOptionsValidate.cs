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

            // validate scenario file
            ValidateScenarioFile(harnessOptions, ref valid, errorMessage);

            // validate worker executable
            ValidateWorkerExecutable(harnessOptions, ref valid, errorMessage);

            // validate language executable
            ValidateLanguageExecutable(harnessOptions, ref valid, errorMessage);

            // validate worker directory
            ValidateWorkerDirectory(harnessOptions, ref valid, errorMessage);

            return valid;
        }

        private void ValidateWorkerDirectory(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.WorkerDirectory))
            {
                _logger.LogError(errorMessage, "workerDirectory");
                valid = false;
            }
            else
            {
                harnessOptions.WorkerDirectory = Path.GetFullPath(harnessOptions.WorkerDirectory);

                if (!Directory.Exists(harnessOptions.WorkerDirectory))
                {
                    _logger.LogError(errorMessage, "workerDirectory");
                    valid = false;
                }
            }
        }

        private void ValidateLanguageExecutable(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.LanguageExecutable))
            {
                _logger.LogError(errorMessage, "languageExecutable");
                valid = false;
            }
            else
            {
                harnessOptions.LanguageExecutable = Path.GetFullPath(harnessOptions.LanguageExecutable);

                if (!File.Exists(harnessOptions.LanguageExecutable))
                {
                    _logger.LogError(errorMessage, "languageExecutable");
                    valid = false;
                }
            }
        }

        private void ValidateWorkerExecutable(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.WorkerExecutable))
            {
                _logger.LogError(errorMessage, "workerExecutable");
                valid = false;
            }
            else
            {
                harnessOptions.WorkerExecutable = Path.GetFullPath(harnessOptions.WorkerExecutable);

                if (!File.Exists(harnessOptions.WorkerExecutable))
                {
                    _logger.LogError(errorMessage, "workerExecutable");
                    valid = false;
                }
            }
        }

        private void ValidateScenarioFile(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.ScenarioFile))
            {
                _logger.LogError(errorMessage, "scenarioFile");
                valid = false;
            }
            else
            {
                harnessOptions.ScenarioFile = Path.GetFullPath(harnessOptions.ScenarioFile);

                if (!File.Exists(harnessOptions.ScenarioFile))
                {
                    _logger.LogError(errorMessage, "scenarioFile");
                    valid = false;
                }
            }
        }
    }
}
