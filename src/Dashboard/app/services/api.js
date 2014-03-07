angular.module('dashboard').service('api', function () {
    return {
        kudu: {
            jobs: function () {
                return '/jobs';
            },
            job: function (type, name) {
                if (type !== 'triggered' && type !== 'continuous') {
                    return "";
                }
                return '/jobs/' + type + '/' + encodeURIComponent(name);
            },
            jobRun: function (jobName, runId) {
                return '/jobs/triggered/' + encodeURIComponent(jobName) + '/history/' + encodeURIComponent(runId);
            },
            jobHistory: function (type, name) {
                var base = this.job(type, name);
                if (base === "") {
                    return "";
                }
                return base + '/history';
            },
        },
        sdk: {
            abortFunctionInstance: function (invocationId) {
                return "api/functions/invocations/" + encodeURIComponent(invocationId) + "/abort";
            },
            recentInvocations: function () {
                return "api/functions/invocations/recent";
            },
            invocationsByFunction: function (functionName) {
                return "api/functions/definitions/" + encodeURIComponent(functionName) + "/invocations";
            },
            functionDefinition: function (functionName) {
                return "api/functions/definitions/" + encodeURIComponent(functionName);
            },
            functionDefinitions: function () {
                return "api/functions/definitions";
            },
            invocationByIds: function () {
                return "api/functions/invocationsByIds";
            },
            functionInvocation: function (invocationId) {
                return "api/functions/invocations/" + encodeURIComponent(invocationId);
            },
            getFunctionInvocationChildren: function (invocationId) {
                return "api/functions/invocations/" + encodeURIComponent(invocationId) + "/children";
            },
            functionConsoleLog: function (invocationId) {
                return "api/log/output/" + encodeURIComponent(invocationId);
            },
            downloadBlob: function (blobPath) {
                return "api/log/blob?path=" + encodeURIComponent(blobPath);
            },
            functionsInJob: function (jobType, jobName, runId) {
                if (jobType !== 'triggered' && jobType !== 'continuous') {
                    throw "Unsuppoerted job type " + jobType;
                }
                if (jobType === 'triggered') {
                    return 'api/jobs/triggered/' + jobName + '/runs/' + runId + '/functions';
                }
                return 'api/jobs/continuous/' + jobName + '/functions';
            }
        }
    };
});