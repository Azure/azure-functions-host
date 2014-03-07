angular.module('dashboard').controller('ContinuousJobController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionInvocationSummary, JobDefinition, api, urls) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            jobName = $routeParams.jobName,
            functionsInJobUrl = api.sdk.functionsInJob('continuous', jobName),
            functionInvocationsUrl = api.sdk.invocationByIds(),
            jobRunUrl = api.kudu.job('continuous', jobName);

        $scope.jobName = jobName;
        $scope.nonFinalInvocations = {};

        $scope.breadcrumbs = [{
            url: urls.jobs(),
            title: 'Jobs'
        }];

        function getInitialFunctionInvocations() {
            return getFunctionInvocations({ limit: 100 });
        }

        function getNewFunctionInvocations() {
            if ($scope.invocations && $scope.invocations.length && $scope.invocations.length > 0) {
                getFunctionInvocations({ limit: 100, newerThan: $scope.invocations[0].rowKeyForJobRunLookup }, true);
            }
        }

        function getFunctionInvocations(params, addToTop) {
            return $http.get(functionsInJobUrl, { params: params }).then(function (res) {
                var len = res.data.length,
                    ix,
                    item,
                    invocation;
                $scope.invocations = $scope.invocations || [];
                if (len === 0) {
                    return;
                }
                if (addToTop) {
                    for (ix = len - 1; ix !== -1; --ix) {
                        item = res.data[ix];
                        invocation = FunctionInvocationSummary.fromJson(item.invocation);
                        invocation.rowKeyForJobRunLookup = item.rowKey;
                        $scope.invocations.unshift(invocation);
                        if (!invocation.isFinal()) {
                            $scope.nonFinalInvocations[invocation.id] = invocation;
                        }
                    }
                } else {
                    for (ix = 0; ix !== len; ++ix) {
                        item = res.data[ix];
                        invocation = FunctionInvocationSummary.fromJson(item.invocation);
                        invocation.rowKeyForJobRunLookup = item.rowKey;
                        $scope.invocations.push(invocation);
                    }
                }
            });
        }

        function getUpdatedFunctionInvocations() {
            var nonFinalIds = Object.getOwnPropertyNames($scope.nonFinalInvocations);
            if (nonFinalIds.length === 0) {
                return;
            }
            $http.post(functionInvocationsUrl, JSON.stringify(nonFinalIds)).then(function (res) {
                var len = res.data.length,
                    ix,
                    item,
                    invocation;
                if (len === 0) {
                    return;
                }
                for (ix = 0; ix !== len; ++ix) {
                    item = res.data[ix];
                    invocation = $scope.nonFinalInvocations[item.id];
                    if (invocation) {
                        invocation.functionDisplayTitle = item.functionDisplayTitle;
                        invocation.status = item.status;
                        invocation.when = item.whenUtc;
                        invocation.duration = item.duration;
                        invocation.exceptionMessage = item.exceptionMessage;
                        invocation.updateTimingStrings();
                        if (invocation.isFinal()) {
                            delete $scope.nonFinalInvocations[invocation.id];
                        }
                    }
                }
            });
        }

        function getJobRunDetails() {
            return $http.get(jobRunUrl).then(function (res) {
                $scope.job = new JobDefinition(res.data);
            });
        }

        function getInitialData() {
            var jobRunDone, initialFunctionsDone;

            if ($scope._sdkNotConfigured) {
                initialFunctionsDone = true;
            } else {
                getInitialFunctionInvocations().then(function () {
                    initialFunctionsDone = true;
                    if (jobRunDone) {
                        startPolling();
                    }
                });
            }

            getJobRunDetails().then(function () {
                jobRunDone = true;
                if (initialFunctionsDone) {
                    startPolling();
                }
            });
        }

        function getData() {
            $scope.job = $scope.job || {};
            // TODO: look up all antares WebJobs statuses that are "not running"
            if ($scope.job.status === 'Success') {
                return;
            }
            getJobRunDetails();
            if (!$scope._sdkNotConfigured) {
                getNewFunctionInvocations();
                getUpdatedFunctionInvocations();
            }
        }

        function updateTiming() {
            var ix, len = $scope.invocations.length;
            for (ix = 0; ix !== len; ++ix) {
                $scope.invocations[ix].updateTimingStrings();
            }
        }

        getInitialData();

        function startPolling() {
            poll = $interval(function () {
                if (((new Date()) - lastPoll) > pollInterval) {
                    lastPoll = new Date();
                    getData();
                }
                updateTiming();
            }, 2000);
        }

        $scope.$on('$destroy', function () {
            // Make sure that the interval is destroyed too
            if (poll) {
                $interval.cancel(poll);
                poll = undefined;
            }
        });
    }
);
