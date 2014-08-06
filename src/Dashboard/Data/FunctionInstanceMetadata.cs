// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dashboard.Data
{
    public static class FunctionInstanceMetadata
    {
        public static readonly string DisplayTitle = "displayTitle";

        public static readonly string StartTime = "startTime";

        public static readonly string EndTime = "endTime";

        public static readonly string Succeeded = "succeeded";

        public static readonly string HeartbeatContainer = "heartbeatContainer";

        public static readonly string HeartbeatDirectory = "heartbeatDirectory";

        public static readonly string HeartbeatName = "heartbeatName";

        public static readonly string HeartbeatExpiration = "heartbeatExpiration";

        internal static Dictionary<string, string> CreateFromSnapshot(FunctionInstanceSnapshot snapshot)
        {
            var metadata = new Dictionary<string, string>();

            metadata.Add(FunctionInstanceMetadata.DisplayTitle, snapshot.DisplayTitle);

            if (snapshot.StartTime.HasValue)
            {
                metadata.Add(FunctionInstanceMetadata.StartTime, SerializeDateTimeOffset(snapshot.StartTime.Value));
            }
            if (snapshot.EndTime.HasValue)
            {
                metadata.Add(FunctionInstanceMetadata.EndTime, SerializeDateTimeOffset(snapshot.EndTime.Value));
            }
            if (snapshot.Succeeded.HasValue)
            {
                metadata.Add(FunctionInstanceMetadata.Succeeded, snapshot.Succeeded.Value.ToString());
            }
            if (snapshot.Heartbeat != null)
            {
                metadata.Add(FunctionInstanceMetadata.HeartbeatContainer, snapshot.Heartbeat.SharedContainerName);
                metadata.Add(FunctionInstanceMetadata.HeartbeatDirectory, snapshot.Heartbeat.SharedDirectoryName);
                metadata.Add(FunctionInstanceMetadata.HeartbeatName, snapshot.Heartbeat.InstanceBlobName);
                metadata.Add(FunctionInstanceMetadata.HeartbeatExpiration, snapshot.Heartbeat.ExpirationInSeconds.ToString());
            }
            return metadata;
        }

        private static string SerializeDateTimeOffset(DateTimeOffset dto)
        {
            return dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }

        internal static DateTimeOffset? DeserializeDateTimeOffset(string s)
        {
            DateTimeOffset result;
            if (DateTimeOffset.TryParseExact(s, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }
            return null;
        }
    }
}