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
            abortHostInstance: function (instanceQueueName) {
                return "api/hostInstances/" + encodeURIComponent(instanceQueueName) + "/abort";
            },
            recentInvocations: function () {
                return "api/functions/invocations/recent";
            },
            invocationsByFunction: function (functionId) {
                return "api/functions/definitions/" + encodeURIComponent(functionId) + "/invocations";
            },
            functionDefinition: function (functionId) {
                return "api/functions/definitions/" + encodeURIComponent(functionId);
            },
            functionDefinitions: function () {
                return "api/functions/definitions";
            },
            functionNewerDefinitions: function (version) {
                return "api/functions/newerDefinitions?version=" + encodeURIComponent(version);
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
                    throw "Unsupported WebJob type " + jobType;
                }
                if (jobType === 'triggered') {
                    return 'api/jobs/triggered/' + jobName + '/runs/' + runId + '/functions';
                }
                return 'api/jobs/continuous/' + jobName + '/functions';
            },
            indexerLogs: function () {
                return "api/diagnostics/indexerLogs";
            },
            indexerLogEntry: function (entryId) {
                return "api/diagnostics/indexerLogEntry?entryId=" + encodeURIComponent(entryId);
            },
            indexingQueueLength: function (limit) {
                return "api/diagnostics/indexingQueueLength" + (limit !== null ? "?limit=" + limit : "");
            }
        }
    };
});
