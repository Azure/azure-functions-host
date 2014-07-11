angular.module('dashboard').service('stringUtils', function () {

    function plural(singularPhrase, pluralPhrase, count) {
        return count === 1
            ? singularPhrase
            : pluralPhrase.replace(/\{0\}/g, count);
    }

    function toDateTime(dateTime) {
        if (dateTime instanceof Date) {
            return dateTime;
        }
        if (typeof (dateTime) === 'string') {
            dateTime = new Date(Date.parse(dateTime));
        }
        if (!(dateTime instanceof Date)) {
            throw new Error("dateTime should be a Date object, or a string that can be parsed by Date.parse.");
        }
        return dateTime;
    }
    function formatTimeSpan(timeSpan) {
        var totalSeconds = timeSpan / 1000;
        var totalMinutes = totalSeconds / 60;
        var totalHours = totalMinutes / 60;
        var totalDays = totalHours / 24;
        var totalWeeks = totalDays / 7;
        if (totalDays > 7)
            return plural("1 week", "{0} weeks", Math.round(totalWeeks));

        if (totalHours > 24)
            return plural("1 day", "{0} days", Math.round(totalDays));

        if (totalMinutes > 60)
            return plural("1 hour", "{0} hours", Math.round(totalHours));

        if (totalSeconds > 60)
            return plural("1 minute", "{0} minutes", Math.round(totalMinutes));

        if (totalSeconds > 10)
            return plural("1 second", "{0} seconds", Math.round(totalSeconds));

        if (timeSpan > 1000)
            return plural("1 s", "{0} s", Math.round(totalSeconds));

        if (timeSpan > 0)
            return plural("1 ms", "{0} ms", timeSpan);

        return "less than 1ms";
    }

    function formatDateTime(dateTime) {
        dateTime = toDateTime(dateTime);
        var now = new Date();
        var time = now - dateTime;

        var totalSeconds = time / 1000;
        var totalMinutes = totalSeconds / 60;
        var totalHours = totalMinutes / 60;
        var totalDays = totalHours / 24;
        if (totalDays > 6.5 || totalDays < -6.5)
            return dateTime.toLocaleDateString() + ' ' + dateTime.toLocaleTimeString();
        if (totalHours > 24)
            return plural("1 day ago", "{0} days ago", Math.round(totalDays));
        if (totalHours < -24)
            return plural("in 1 day", "in {0} days", 0 - Math.round(totalDays));
        if (totalMinutes > 60)
            return plural("1 hour ago", "{0} hours ago", Math.round(totalHours));
        if (totalMinutes < -60)
            return plural("in 1 hour", "in {0} hours", 0 - Math.round(totalHours));

        if (totalSeconds > 60)
            return plural("1 minute ago", "{0} minutes ago", Math.round(totalMinutes));
        if (totalSeconds < -60)
            return plural("in 1 minute", "in {0} minutes", 0 - Math.round(totalMinutes));

        if (totalSeconds > 10)
            return plural("1 second ago", "{0} seconds ago", Math.round(totalSeconds)); //aware that the singular won't be used
        if (totalSeconds < -10)
            return plural("in 1 second", "in {0} seconds", 0 - Math.round(totalSeconds));

        return time > 0
            ? "a moment ago"
            : "in a moment";
    }

    return {
        formatTimingString: function (time, duration) {
            return formatDateTime(time) + " (" + formatTimeSpan(duration) + ")";
        },
        formatTimeSpan: formatTimeSpan,
        toDateTime: toDateTime,
        formatDateTime: formatDateTime
    };
});
