angular.module('dashboard').service('invocationUtils', function() {
    return {
        getStatusText: function(invocation) {
            switch (invocation.status) {
            case "CompletedFailed":
                return "Failed";
            case "CompletedSuccess":
                return "Success";
            case "NeverFinished":
                return "Never Finished";
            case "Running":
                return "Running";
            default:
                return invocation.status;
            }
        },
        getStatusLabelClass: function(invocation) {
            switch (invocation.status) {
            case "CompletedFailed":
                return "label-danger";
            case "CompletedSuccess":
                return "label-success";
            case "NeverFinished":
                return "label-warning";
            case "Running":
                return "label-primary";
            default:
                return "label";
            }
        }
    };
});
