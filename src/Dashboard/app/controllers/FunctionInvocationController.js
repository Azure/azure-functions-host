angular.module('dashboard').controller('FunctionInvocationController',
    function ($rootScope, $scope, $routeParams, $location, $interval, $http, $q, stringUtils, invocationUtils, api, FunctionInvocationModel, FunctionInvocationSummary, urls, isUsingSdk) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            invocationId = $routeParams.invocationId,
            functionInvocationDataUrl = api.sdk.functionInvocation(invocationId);

        isUsingSdk.setUsing($scope);
        $scope.invocationId = invocationId;
        $scope.stringUtils = stringUtils;

        $scope.breadcrumbs = [{
            url: urls.functions(),
            title: 'Functions'
        }];

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.abort = function() {
            $scope.aborting = true;
            $http({
                method: "POST",
                url: api.sdk.abortHostInstance($scope.model.invocation.instanceQueueName)
            }).then(function() {
                $scope.model.isAborted = true;
                delete $scope.aborting;
            }, function() {
                delete $scope.aborting;
            });
        };

        function loadInvocationDetails() {
            var runId;
            if (!$scope.model || !$scope.model.invocation.isFinal()) {
                return $http.get(functionInvocationDataUrl).success(function (res) {
                    var initialLoad = !$scope.model;
                    $scope.model = FunctionInvocationModel.fromJson(res);
                    if (initialLoad) {
                        // generate breadcrumb on initial load
                        if (res.invocation.executingJobRunId) {
                            runId = res.invocation.executingJobRunId;
                            $scope.breadcrumbs = [{
                                    url: urls.jobs(),
                                    title: 'WebJobs'
                                }, {
                                    url: urls.job(runId.jobType.toLowerCase(), runId.jobName),
                                    title: runId.jobName
                                }];
                            if (runId.runId && runId.jobType.toLowerCase() === 'triggered') {
                                $scope.breadcrumbs.push({
                                    url: urls.triggeredJobRun(runId.jobName, runId.runId),
                                    title: 'Run'
                                });
                            }
                            $scope.breadcrumbs.push({
                                url: urls.functionDefinition($scope.model.invocation.functionId),
                                title: $scope.model.invocation.functionName
                            });
                        } else {
                            $scope.breadcrumbs = [{
                                    url: urls.functions(),
                                    title: 'Functions'
                                }, {
                                    url: urls.functionDefinition($scope.model.invocation.functionId),
                                    title: $scope.model.invocation.functionName
                                }];
                        }
                    }
                }).error(function (res, code) {
                    if (code === 404) {
                        $rootScope.errors.push('Invalid function invocation');
                    } else if (res.exceptionMessage) {
                        $rootScope.errors.push(res.exceptionMessage);
                    } else {
                        $rootScope.errors.push('Invalid function invocation (Error code: ' + code + ')');
                    }

                    $location.url('/functions');
                });
            } else {
                var deferred = $q.defer();
                deferred.resolve();
                return deferred.promise;
            }
        }

        function getData() {
            lastPoll = new Date();
            loadInvocationDetails().then(function() {
                $scope.$broadcast('invocations:poll');
            });
        }

        $scope.invocations = {
            endpoint: api.sdk.getFunctionInvocationChildren(invocationId),
        };

        getData();
        poll = $interval(function () {
            if (((new Date()) - lastPoll) > pollInterval) {
                getData();
            }

            // skip children hasNew lookup if the invocation had completed 20 minutes ago or more.
            if ($scope.model && $scope.model.invocation && (new Date() - $scope.model.invocation.when) > 1000 * 60 * 20) {
                $scope.invocations.skipHasNewPolling = true;
            }
            $scope.$broadcast('invocations:updateTiming');
        }, 2000);

        $scope.$on('$destroy', function () {
            // Make sure that the interval is destroyed too
            if (poll) {
                $interval.cancel(poll);
                poll = undefined;
            }
        });
    }
);
