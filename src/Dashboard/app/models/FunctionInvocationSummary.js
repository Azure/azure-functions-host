angular.module('dashboard').factory('FunctionInvocationSummary', function (stringUtils) {
    function FunctionInvocationSummary() {
    }

    FunctionInvocationSummary.fromJson = function (item) {
        var func = new FunctionInvocationSummary();
        func.id = item.id;
        func.functionName = item.functionName;
        func.functionFullName = item.functionFullName;
        func.functionDisplayTitle = item.functionDisplayTitle;
        func.status = item.status;
        func.when = stringUtils.toDateTime(item.whenUtc);
        func.duration = item.duration;
        func.exceptionMessage = item.exceptionMessage;
        func.updateTimingStrings();
        return func;
    };

    FunctionInvocationSummary.prototype.updateTimingStrings = function () {
        this.statusTimeString = stringUtils.formatDateTime(this.when);
        this.durationString = stringUtils.formatTimeSpan(this.duration);
    };

    FunctionInvocationSummary.prototype.isFinal = function () {
        return this.status.indexOf("Completed") === 0 || this.status.indexOf("Never") === 0;
    };

    FunctionInvocationSummary.prototype.isRunning = function () {
        return this.status === "Running";
    };

    FunctionInvocationSummary.prototype.getLabelClass = function () {
        switch (this.status) {
            case "CompletedFailed":
                return "label-danger";
            case "CompletedSuccess":
                return "label-success";
            case "NeverFinished":
                return "label-warning";
            case "Running":
            case "Queued":
                return "label-primary";
            default:
                return "label";
        }
    };

    FunctionInvocationSummary.prototype.getAlertClass = function () {
        switch (this.status) {
            case "CompletedFailed":
                return "alert-danger";
            case "CompletedSuccess":
                return "alert-success";
            case "NeverFinished":
                return "alert-warning";
            case "Running":
            case "Queued":
                return "alert-info";
            default:
                return "alert";
        }
    };

    FunctionInvocationSummary.prototype.getDescriptionClass = function () {
        switch (this.status) {
            case "CompletedFailed":
                return "text-error";
            case "CompletedSuccess":
                return "text-success";
            case "NeverFinished":
                return "text-error";
            case "Running":
                return "text-info";
            default:
                return "text";
        }
    };

    FunctionInvocationSummary.prototype.getLabelText = function () {
        switch (this.status) {
            case "CompletedFailed":
                return "Failed";
            case "CompletedSuccess":
                return "Success";
            case "NeverFinished":
                return "Never Finished";
            case "Running":
                return "Running";
            default:
                return this.status;
        }
    };

    return (FunctionInvocationSummary);
});