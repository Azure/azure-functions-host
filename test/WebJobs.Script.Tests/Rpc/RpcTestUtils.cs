// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public static class RpcTestUtils
    {
        public static WorkerDescription GetTestDefaultWorkerDescription(string language, string[] arguments, string testWorkerPathInWorkerConfig)
        {
            return new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = $"{testWorkerPathInWorkerConfig}.{language}",
                Language = language,
                Extensions = new List<string> { $".{language}" },
                Arguments = arguments.ToList()
            };
        }

        public static JObject GetTestWorkerConfigWithProfiles(string language, string[] arguments, string testWorkerPathInWorkerConfig)
        {
            JObject workerConfig = new JObject();
            workerConfig["description"] = JObject.FromObject(GetTestDefaultWorkerDescription(language, arguments, testWorkerPathInWorkerConfig));

            JObject cutomExePath = new JObject();
            cutomExePath["defaultExecutablePath"] = "myFooPath";

            JObject testProfile = new JObject();
            testProfile["testProfile"] = cutomExePath;

            workerConfig["profiles"] = testProfile;

            return workerConfig;
        }
    }
}
