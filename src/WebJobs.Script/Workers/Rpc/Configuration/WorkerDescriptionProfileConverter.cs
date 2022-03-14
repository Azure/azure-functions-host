// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class WorkerDescriptionProfileConverter : JsonConverter
    {
        private ILogger _logger;
        private ISystemRuntimeInformation _systemRuntimeInformation;
        private IEnvironment _environment;

        public WorkerDescriptionProfileConverter(ILogger logger, ISystemRuntimeInformation systemRuntimeInformation, IEnvironment environment)
        {
            _logger = logger;
            _systemRuntimeInformation = systemRuntimeInformation;
            _environment = environment;
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(WorkerDescriptionProfile).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jObject = JObject.Load(reader);
            WorkerDescriptionProfile workerDescriptionProfile = new WorkerDescriptionProfile();
            workerDescriptionProfile.Name = (string)jObject["name"];
            workerDescriptionProfile.ProfileDescription = jObject.Property(WorkerConstants.WorkerDescription).Value.ToObject<RpcWorkerDescription>();

            WorkerProfileConditionFactory workerProfileConditionFactory = new WorkerProfileConditionFactory(_logger, _systemRuntimeInformation, _environment);
            workerDescriptionProfile.Conditions = new List<IWorkerProfileCondition>();

            JArray conditions = jObject.GetValue("conditions") as JArray;

            foreach (JObject conditionJObject in conditions)
            {
                IWorkerProfileCondition workerProfileCondition = workerProfileConditionFactory.CreateWorkerProfileCondition((string)conditionJObject["type"], (string)conditionJObject["name"], (string)conditionJObject["expression"]);
                workerDescriptionProfile.Conditions.Add(workerProfileCondition);
            }
            workerDescriptionProfile.Validate();
            return workerDescriptionProfile;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}