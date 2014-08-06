// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dashboard.Data
{
    public class RecentInvocationEntry
    {
        private readonly DateTimeOffset _timestamp;
        private readonly Guid _id;
        private readonly IDictionary<string, string> _metadata;

        public RecentInvocationEntry(DateTimeOffset timestamp, Guid id, IDictionary<string, string> metadata)
        {
            _timestamp = timestamp;
            _id = id;
            _metadata = metadata;
        }

        public DateTimeOffset Timestamp
        {
            get { return _timestamp; }
        }

        public Guid Id
        {
            get { return _id; }
        }

        public IDictionary<string, string> Metadata
        {
            get { return _metadata; }
        }

        public override string ToString()
        {
            return Format(_timestamp, _id);
        }

        public static string Format(DateTimeOffset timestamp, Guid id)
        {
            long currentTicks = timestamp.UtcDateTime.Ticks; // Ticks must be UTC-relative.
            long reverseTicks = DateTimeOffset.MaxValue.Ticks - currentTicks;

            // DateTimeOffset.MaxValue.Ticks.ToString().Length = 19
            // Subtract from DateTimeOffset.MaxValue.Ticks to have newer times sort at the top.
            return String.Format(CultureInfo.InvariantCulture, "{0:D19}_{1:N}", reverseTicks, id);
        }

        public static RecentInvocationEntry Parse(string input, IDictionary<string, string> metadata)
        {
            RecentInvocationEntry parsed;

            if (!TryParse(input, metadata, out parsed))
            {
                throw new FormatException("Recent function instance blob names must be in the format timestamp_guid.");
            }

            return parsed;
        }

        private static bool TryParse(string input, IDictionary<string, string> metadata, out RecentInvocationEntry parsed)
        {
            if (input == null)
            {
                parsed = null;
                return false;
            }

            int underscoreIndex = input.IndexOf('_');

            // There must be at least one character before the understore and one character after the understore.
            if (underscoreIndex <= 0 || underscoreIndex > input.Length - 1)
            {
                parsed = null;
                return false;
            }

            string reverseTicksPortion = input.Substring(0, underscoreIndex);
            string idPortion = input.Substring(underscoreIndex + 1);

            long reverseTicks;
            
            if (!Int64.TryParse(reverseTicksPortion, NumberStyles.None, CultureInfo.InvariantCulture, out reverseTicks))
            {
                parsed = null;
                return false;
            }

            Guid id;

            if (!Guid.TryParseExact(idPortion, "N", out id))
            {
                parsed = null;
                return false;
            }

            long ticks = DateTimeOffset.MaxValue.Ticks - reverseTicks; // Recompute the original ticks.
            DateTimeOffset timestamp = new DateTimeOffset(reverseTicks, TimeSpan.Zero); // Ticks must be UTC-relative.

            parsed = new RecentInvocationEntry(timestamp, id, metadata);
            return true;
        }
    }
}
