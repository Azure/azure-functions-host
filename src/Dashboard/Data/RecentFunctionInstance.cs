using System;
using System.Globalization;

namespace Dashboard.Data
{
    public class RecentFunctionInstance
    {
        private readonly DateTimeOffset _timestamp;
        private readonly Guid _id;

        public RecentFunctionInstance(DateTimeOffset timestamp, Guid id)
        {
            _timestamp = timestamp;
            _id = id;
        }

        public DateTimeOffset Timestamp
        {
            get { return _timestamp; }
        }

        public Guid Id
        {
            get { return _id; }
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

        public static RecentFunctionInstance Parse(string input)
        {
            RecentFunctionInstance parsed;

            if (!TryParse(input, out parsed))
            {
                throw new FormatException("Recent function instance blob names must be in the format timestamp_guid.");
            }

            return parsed;
        }

        private static bool TryParse(string input, out RecentFunctionInstance parsed)
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

            parsed = new RecentFunctionInstance(timestamp, id);
            return true;
        }
    }
}
