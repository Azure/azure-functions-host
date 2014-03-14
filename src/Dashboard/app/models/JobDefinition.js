angular.module('dashboard').factory('JobDefinition', function (stringUtils) {
    function JobDefinition(item) {
        this.name = item.name;
        this.type = item.type;
        this.runCommand = item.run_command;
        this.usingSdk = !!item.using_sdk;
        switch (item.type) {
            case 'triggered':
                initializeTriggeredJob(this, item);
                break;
            case 'continuous':
                initializeContinuousJob(this, item);
                break;
            default:
                throw "Unknown job type " + item.type;
        }

        this.updateStrings();
    }

    function initializeTriggeredJob(job, item) {
        if (item.latest_run) {
            job.status = item.latest_run.status;
            job.lastRunStartTime = item.latest_run.start_time ? stringUtils.toDateTime(item.latest_run.start_time) : null;
            job.lastRunEndTime = item.latest_run.end_time ? stringUtils.toDateTime(item.latest_run.end_time) : null;
        }
    }

    function initializeContinuousJob(job, item) {
        job.status = item.status;
        job.outUrl = item.log_url;
    }

    JobDefinition.prototype.updateStrings = function () {
        switch (this.type) {
            case "triggered":
                if (!this.lastRunStartTime) {
                    this.lastRunTimeString = "Never ran";
                } else {
                    var duration = (this.lastRunEndTime || Date()) - this.lastRunStartTime;
                    this.lastRunTimeString = stringUtils.formatTimingString(this.lastRunStartTime, duration);
                }
                break;
            case "continuous":
                this.lastRunTimeString = "Runs continuously";
                break;
            default:
                this.lastRunTimeString = "unknown";
        }
    };

    JobDefinition.prototype.getStatusClass = function () {
        return JobDefinition.getStatusClass(this);
    };

    JobDefinition.prototype.getStatusText = function () {
        return JobDefinition.getStatusText(this);
    };

    JobDefinition.prototype.getAlertClass = function () {
        return JobDefinition.getAlertClass(this);
    };

    JobDefinition.prototype.getAlertHeader = function () {
        return JobDefinition.getAlertHeader(this);
    };

    JobDefinition.getStatusClass = function (job) {
        switch (job.status) {
            case "Running":
                return "label-primary";
            case "Success":
                return "label-success";
            case "Starting":
            case "Initializing":
            case "Stopped":
            case "Disabling":
            case "Stopping":
            case "PendingRestart":
                return "label-warning";
            case "InactiveInstance":
            case "Failed":
            case "Aborted":
                return "label-danger";
            default:
                return "label";
        }
    };

    JobDefinition.getStatusText = function (job) {
        switch (job.status) {
            case "PendingRestart":
                return "Pending restart";
            case "InactiveInstance":
                return "Inactive instance";
            default:
                return job.status;
        }
    };


    JobDefinition.getAlertClass = function (job) {
        switch (job.status) {
            case "Running":
                return "alert-info";
            case "Success":
                return "alert-success";
            case "Starting":
            case "Initializing":
            case "Stopped":
            case "Disabling":
            case "Stopping":
            case "PendingRestart":
                return "alert-warning";
            case "InactiveInstance":
            case "Failed":
            case "Aborted":
                return "alert-danger";
            default:
                return "label";
        }
    };

    JobDefinition.getAlertHeader = JobDefinition.getStatusText;
    JobDefinition.isRunning = function (job) {
        return job.status === "Running";
    };
    return (JobDefinition);
});