angular.module('dashboard').factory('JobRun', function (stringUtils, JobDefinition) {
    function JobRun(id, status, startTime, endTime, duration, outUrl, errUrl) {
        this.id = id;
        this.startTime = stringUtils.toDateTime(startTime);
        if (endTime) {
            this.endTime = stringUtils.toDateTime(endTime);
        }
        this.status = status;
        this.outUrl = outUrl;
        this.errUrl = errUrl;
        this.updateTimingStrings();
    };

    JobRun.prototype.updateTimingStrings = function() {
        var duration = (this.endTime || Date()) - this.startTime;
        this.startTimeString = stringUtils.formatDateTime(this.startTime);
        this.durationString = stringUtils.formatTimeSpan(duration);
        this.timingString = stringUtils.formatTimingString(this.startTime, duration);
    };

    JobRun.prototype.getStatusClass = function () {
        return JobDefinition.getStatusClass(this);
    };

    JobRun.prototype.getStatusText = function () {
        return JobDefinition.getStatusText(this);
    };
    
    JobRun.prototype.getAlertClass = function () {
        return JobDefinition.getAlertClass(this);
    };

    JobRun.prototype.getAlertHeader = function () {
        return JobDefinition.getAlertHeader(this);
    };

    JobRun.prototype.isRunning = function () {
        return JobDefinition.isRunning(this);
    };

    return (JobRun);
});
