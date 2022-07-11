// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerConfigTestUtilities
    {
        public const string TestWorkerPathInWorkerConfig = "./src/index";
        public const string TestDefaultWorkerFile = "testWorker.py";
        public const string HttpWorkerExe = "httpServer.exe";
        public const string TestDefaultExecutablePath = "testWorkerPath";

        public static JObject GetTestWorkerConfig(string language, string[] arguments, bool invalid, string profileName, bool invalidProfile, bool emptyWorkerPath = false)
        {
            WorkerDescription description = GetTestDefaultWorkerDescription(language, arguments);

            JObject config = new JObject();
            config[WorkerConstants.WorkerDescription] = JObject.FromObject(description);

            if (!string.IsNullOrEmpty(profileName))
            {
                var appSvcDescription = new RpcWorkerDescription()
                {
                    DefaultExecutablePath = "myFooPath",
                };

                var profiles = new JArray();
                var profile = new JObject();
                var conditions = new JArray();
                profile[WorkerConstants.WorkerDescriptionProfileName] = "profileName";
                if (invalidProfile)
                {
                    conditions.Add(ProfilesTestUtilities.GetTestWorkerProfileCondition(WorkerConstants.WorkerDescriptionProfileHostPropertyCondition, "hostVersion", "-1"));
                }
                conditions.Add(ProfilesTestUtilities.GetTestWorkerProfileCondition());
                profile[WorkerConstants.WorkerDescriptionProfileConditions] = conditions;
                profile[WorkerConstants.WorkerDescription] = JObject.FromObject(appSvcDescription);
                profiles.Add(profile);
                config[WorkerConstants.WorkerDescriptionProfiles] = profiles;
            }

            if (invalid)
            {
                config[WorkerConstants.WorkerDescription] = "invalidWorkerConfig";
            }

            if (emptyWorkerPath)
            {
                config[WorkerConstants.WorkerDescription][WorkerConstants.WorkerDescriptionDefaultWorkerPath] = null;
            }

            return config;
        }

        public static RpcWorkerDescription GetTestDefaultWorkerDescription(string language, string[] arguments)
        {
            return new RpcWorkerDescription()
            {
                DefaultExecutablePath = TestDefaultExecutablePath,
                DefaultWorkerPath = $"{TestWorkerPathInWorkerConfig}.{language}",
                Language = language,
                Extensions = new List<string> { $".{language}" },
                Arguments = arguments.ToList()
            };
        }

        public static WorkerDescription GetTestHttpInvokerDescription(string[] arguments, bool invalid = false)
        {
            if (invalid)
            {
                return new RpcWorkerDescription();
            }
            RpcWorkerDescription workerDescription = new RpcWorkerDescription()
            {
                DefaultExecutablePath = HttpWorkerExe
            };
            if (arguments != null)
            {
                workerDescription.Arguments = arguments.ToList();
            }
            return workerDescription;
        }

        public static void CreateWorkerFolder(string testDir, TestRpcWorkerConfig workerConfig, bool createTestWorker = true)
        {
            string workerPath = string.IsNullOrEmpty(workerConfig.Language) ? testDir : Path.Combine(testDir, workerConfig.Language);
            Directory.CreateDirectory(workerPath);
            File.WriteAllText(Path.Combine(workerPath, RpcWorkerConstants.WorkerConfigFileName), workerConfig.Json);
            if (createTestWorker)
            {
                Directory.CreateDirectory(Path.Combine(workerPath, $"{TestWorkerPathInWorkerConfig}"));
                File.WriteAllText(Path.Combine(workerPath, $"{TestWorkerPathInWorkerConfig}.{workerConfig.Language}"), "test worker");
            }
        }

        public static void CreateTestWorkerFileInCurrentDir()
        {
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), TestDefaultWorkerFile), "Hello test worker");
        }

        public static void DeleteTestDir(string testDir)
        {
            if (Directory.Exists(testDir))
            {
                try
                {
                    Directory.Delete(testDir, true);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        public static void DeleteTestWorkerFileInCurrentDir()
        {
            if (File.Exists(TestWorkerPathInWorkerConfig))
            {
                try
                {
                    File.Delete(TestWorkerPathInWorkerConfig);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }
    }
}
