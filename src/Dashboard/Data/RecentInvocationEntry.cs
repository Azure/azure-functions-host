// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    public class RecentInvocationEntry
    {
        private const string IdKey = "Id";
        private const string DisplayTitleKey = "DisplayTitle";
        private const string StartTimeKey = "StartTime";
        private const string EndTimeKey = "EndTime";
        private const string SucceededKey = "Succeeded";
        private const string HeartbeatSharedContainerNameKey = "HeartbeatSharedContainerName";
        private const string HeartbeatSharedDirectoryNameKey = "HeartbeatSharedDirectoryName";
        private const string HeartbeatInstanceBlobNameKey = "HeartbeatInstanceBlobName";
        private const string HeartbeatExpirationInSecondsKey = "HeartbeatExpirationInSeconds";

        private readonly Guid _id;
        private readonly string _displayTitle;
        private readonly DateTimeOffset? _startTime;
        private readonly DateTimeOffset? _endTime;
        private readonly bool? _succeeded;
        private readonly HeartbeatDescriptor _heartbeat;

        private RecentInvocationEntry(Guid id, string displayTitle, DateTimeOffset? startTime, DateTimeOffset? endTime,
            bool? succeeded, HeartbeatDescriptor heartbeat)
        {
            _id = id;
            _displayTitle = displayTitle;
            _startTime = startTime;
            _endTime = endTime;
            _succeeded = succeeded;
            _heartbeat = heartbeat;
        }

        public Guid Id
        {
            get { return _id; }
        }

        public string DisplayTitle
        {
            get { return _displayTitle; }
        }

        public DateTimeOffset? StartTime
        {
            get { return _startTime; }
        }

        public DateTimeOffset? EndTime
        {
            get { return _endTime; }
        }

        public bool? Succeeded
        {
            get { return _succeeded; }
        }

        public HeartbeatDescriptor Heartbeat
        {
            get { return _heartbeat; }
        }

        public static string CreateBlobName(DateTimeOffset timestamp, Guid id)
        {
            long currentTicks = timestamp.UtcDateTime.Ticks; // Ticks must be UTC-relative.
            long reverseTicks = DateTimeOffset.MaxValue.Ticks - currentTicks;

            // DateTimeOffset.MaxValue.Ticks.ToString().Length = 19
            // Subtract from DateTimeOffset.MaxValue.Ticks to have newer times sort at the top.
            return String.Format(CultureInfo.InvariantCulture, "{0:D19}_{1:N}", reverseTicks, id);
        }

        public static RecentInvocationEntry Create(IDictionary<string, string> metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }

            Guid? id = GetMetadataNullableGuid(metadata, IdKey);

            if (!id.HasValue)
            {
                throw new InvalidOperationException("Invalid recent function instance ID");
            }

            string displayTitle = GetMetadataString(metadata, DisplayTitleKey);
            DateTimeOffset? startTime = GetMetadataNullableDateTimeOffset(metadata, StartTimeKey);
            DateTimeOffset? endTime = GetMetadataNullableDateTimeOffset(metadata, EndTimeKey);
            bool? succeeded = GetMetadataNullableBoolean(metadata, SucceededKey);

            string heartbeatSharedContainerName = GetMetadataString(metadata, HeartbeatSharedContainerNameKey);
            string heartbeatSharedDirectoryName = GetMetadataString(metadata, HeartbeatSharedDirectoryNameKey);
            string heartbeatInstanceBlobName = GetMetadataString(metadata, HeartbeatInstanceBlobNameKey);
            int? heartbeatExpirationInSeconds = GetMetadataNullableInt32(metadata, HeartbeatExpirationInSecondsKey);

            HeartbeatDescriptor heartbeat;

            if (heartbeatSharedContainerName != null && heartbeatSharedDirectoryName != null
                && heartbeatInstanceBlobName != null && heartbeatExpirationInSeconds.HasValue)
            {
                heartbeat = new HeartbeatDescriptor
                {
                    SharedContainerName = heartbeatSharedContainerName,
                    SharedDirectoryName = heartbeatSharedDirectoryName,
                    InstanceBlobName = heartbeatInstanceBlobName,
                    ExpirationInSeconds = heartbeatExpirationInSeconds.Value
                };
            }
            else
            {
                heartbeat = null;
            }

            return new RecentInvocationEntry(id.Value, displayTitle, startTime, endTime, succeeded, heartbeat);
        }

        public static Dictionary<string, string> CreateMetadata(FunctionInstanceSnapshot snapshot)
        {
            var metadata = new Dictionary<string, string>();

            AddMetadataGuid(metadata, IdKey, snapshot.Id);
            AddMetadataString(metadata, DisplayTitleKey, snapshot.DisplayTitle);
            AddMetadataNullableDateTimeOffset(metadata, StartTimeKey, snapshot.StartTime);
            AddMetadataNullableDateTimeOffset(metadata, EndTimeKey, snapshot.EndTime);
            AddMetadataNullableBoolean(metadata, SucceededKey, snapshot.Succeeded);

            HeartbeatDescriptor heartbeat = snapshot.Heartbeat;

            if (heartbeat != null)
            {
                AddMetadataString(metadata, HeartbeatSharedContainerNameKey, heartbeat.SharedContainerName);
                AddMetadataString(metadata, HeartbeatSharedDirectoryNameKey, heartbeat.SharedDirectoryName);
                AddMetadataString(metadata, HeartbeatInstanceBlobNameKey, heartbeat.InstanceBlobName);
                AddMetadataInt32(metadata, HeartbeatExpirationInSecondsKey, heartbeat.ExpirationInSeconds);
            }

            return metadata;
        }

        private static void AddMetadataGuid(IDictionary<string, string> metadata, string key, Guid value)
        {
            metadata.Add(key, value.ToString("N", CultureInfo.InvariantCulture));
        }

        private static void AddMetadataString(IDictionary<string, string> metadata, string key, string value)
        {
            if (value != null)
            {
                metadata.Add(key, value);
            }
        }

        private static void AddMetadataNullableDateTimeOffset(IDictionary<string, string> metadata, string key,
            DateTimeOffset? value)
        {
            if (value.HasValue)
            {
                metadata.Add(key, value.Value.ToString("o", CultureInfo.InvariantCulture));
            }
        }

        private static void AddMetadataNullableBoolean(IDictionary<string, string> metadata, string key, bool? value)
        {
            if (value.HasValue)
            {
                metadata.Add(key, value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AddMetadataInt32(IDictionary<string, string> metadata, string key, int value)
        {
            metadata.Add(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static Guid? GetMetadataNullableGuid(IDictionary<string, string> metadata, string key)
        {
            Debug.Assert(metadata != null);

            if (!metadata.ContainsKey(key))
            {
                return null;
            }

            string unparsed = metadata[key];
            Guid parsed;

            if (!Guid.TryParseExact(unparsed, "N", out parsed))
            {
                return null;
            }

            return parsed;
        }

        private static string GetMetadataString(IDictionary<string, string> metadata, string key)
        {
            Debug.Assert(metadata != null);

            if (!metadata.ContainsKey(key))
            {
                return null;
            }

            return metadata[key];
        }

        private static DateTimeOffset? GetMetadataNullableDateTimeOffset(IDictionary<string, string> metadata,
            string key)
        {
            Debug.Assert(metadata != null);

            if (!metadata.ContainsKey(key))
            {
                return null;
            }

            string unparsed = metadata[key];
            DateTimeOffset parsed;

            if (!DateTimeOffset.TryParseExact(unparsed, "o", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out parsed))
            {
                return null;
            }

            return parsed;
        }

        private static bool? GetMetadataNullableBoolean(IDictionary<string, string> metadata, string key)
        {
            Debug.Assert(metadata != null);

            if (!metadata.ContainsKey(key))
            {
                return null;
            }

            string unparsed = metadata[key];
            bool parsed;

            if (!Boolean.TryParse(unparsed, out parsed))
            {
                return null;
            }

            return parsed;
        }

        private static int? GetMetadataNullableInt32(IDictionary<string, string> metadata, string key)
        {
            Debug.Assert(metadata != null);

            if (!metadata.ContainsKey(key))
            {
                return null;
            }

            string unparsed = metadata[key];
            int parsed;

            if (!Int32.TryParse(unparsed, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
            {
                return null;
            }

            return parsed;
        }
    }
}
