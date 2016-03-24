angular.module('dashboard').service('urls', function (siteRoot, disableInvoke) {
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
        functionInvocation: function(invocationId) {
            return '#/functions/invocations/' + encodeURIComponent(invocationId);
        },
        functions: function() {
            return '#/functions';
        },
        functionDefinition: function(functionId) {
            return '#/functions/definitions/' + encodeURIComponent(functionId);
        },
        replayFunction: disableInvoke ? null : function (invocationId) {
            //return '#/functions/invocations/' + encodeURIComponent(functionId) + '/replay;
            return siteRoot + 'function/replay?parentId=' + encodeURIComponent(invocationId);
        },
        runFunction: disableInvoke ? null : function(functionId) {
            //return '#/functions/definitions/' + encodeURIComponent(functionId) + '/run;
            return siteRoot + 'function/run?functionId=' + encodeURIComponent(functionId);
        },
        indexerLogEntry: function (entryId) {
            return siteRoot + '#/diagnostics/indexerLogEntry/' + encodeURIComponent(entryId);
        }
    };
});
