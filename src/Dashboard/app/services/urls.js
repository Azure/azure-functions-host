angular.module('dashboard').service('urls', function() {
    return {
        jobs: function() {
            return "#/jobs";
        },
        job: function(type, name) {
            if (type !== 'triggered' && type !== 'continuous') {
                return "";
            }
            return "#/jobs/" + type + "/" + encodeURIComponent(name);
        },
        triggeredJobRun: function(jobName, runId) {
            return '#/jobs/triggered/' + encodeURIComponent(jobName) + '/runs/' + encodeURIComponent(runId);
        },
        functionInvocation : function(invocationId) {
            return '#/functions/invocations/' + encodeURIComponent(invocationId);
        },
        functions: function () {
            return '#/functions';
        },
        functionDefinition: function (functionName) {
            return '#/functions/definitions/' + encodeURIComponent(functionName);
        }
    };
});