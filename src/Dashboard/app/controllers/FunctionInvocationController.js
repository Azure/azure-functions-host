angular.module('dashboard').controller('FunctionInvocationController',
    function ($scope, $routeParams, $interval, $http, stringUtils, invocationUtils, api, FunctionInvocationModel, urls) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            invocationId = $routeParams.invocationId,
            functionInvocationDataUrl = api.sdk.functionInvocation(invocationId);

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.invocationId = invocationId;
        $scope.stringUtils = stringUtils;

        $scope.breadcrumbs = [];

        $scope.hasMoreChildren = true;
        $scope.loadChildren = function () {
            $scope.loadingChildren = true;
            loadMoreChildren().then(function (moreChildrenCount) {
                $scope.loadingChildren = false;
                if (moreChildrenCount === 0) {
                    $scope.hasMoreChildren = false;
                }
            }, function (err) {
                $scope.loadingChildren = false;
            });
        };

        function loadInvocationDetails() {
            var runId;
            if (!$scope.model || !$scope.model.invocation.isFinal()) {
                $http.get(functionInvocationDataUrl).success(function (res) {
                    $scope.model = FunctionInvocationModel.fromJson(res);
                    if (res.invocation.executingJobRunId) {
                        runId = res.invocation.executingJobRunId;
                        $scope.breadcrumbs = [{
                            url: urls.jobs(),
                            title: 'Jobs'
                        }, {
                            url: urls.job(runId.jobType.toLowerCase(), runId.jobName),
                            title: runId.jobName
                        }];
                        if (runId.runId) {
                            $scope.breadcrumbs.push({
                                url: urls.jobRun(runId.jobType.toLowerCase(), runId.jobName, runId.runId),
                                title: 'Run'
                            });
                        }
                    } else {
                        $scope.breadcrumbs = [{
                            url: urls.functions(),
                            title: 'Functions'
                        }, {
                            url: urls.functionDefinition($scope.model.invocation.functionFullName),
                            title: $scope.model.invocation.functionName
                        }];
                    }
                });
            }
        }

        function getData() {
            loadInvocationDetails();
            loadChildren();
        }

        // go back in history
        function loadMoreChildren() {
            var deferred = $.Deferred();
            if (!$scope.children || $scope.children.length === 0) {
                deferred.resolve();
                return deferred.promise();
            }

            var params = {
                limit: 20,
                olderThan: $scope.children[$scope.children.length - 1].rowKeyForJobRunLookup
            };
            loadChildrenInternal(params).success(function (res) {
                var ix, len = res.length;
                for (ix = 0; ix !== len; ++ix) {
                    $scope.children.push(res[ix]);
                }
                console.log("resolving: " + res.length);
                deferred.resolve(res.length);
            }).error(function () {
                deferred.reject();
            });
            return deferred.promise();
        }

        function shouldSkipChildrenPolling() {
            // don't skip on running functions.
            if (!$scope.model) {
                return true;
            }
            if ($scope.model.invocation.isRunning()) {
                return false;
            }
            // skip if it has completed 20 minutes ago or more.
            return ((new Date()) - $scope.model.invocation.when > 1000 * 60 * 20);
        }

        // load initial page, and poll for new children
        function loadChildren() {
            var params = {}, ix;
            if (!$scope.children || $scope.children.length === 0) {
                params.limit = 20;
            } else {
                if (shouldSkipChildrenPolling()) {
                    return;
                }
                params.limit = 1000;
                params.newerThan = $scope.children[0].rowKeyForJobRunLookup;
            }
            loadChildrenInternal(params).success(function (res) {
                if (!$scope.children) {
                    $scope.children = res;
                } else {
                    for (ix = res.length - 1; ix !== -1; --ix) {
                        $scope.children.unshift(res[ix]);
                    }
                }
            });
        }

        function loadChildrenInternal(params) {
            return $http({
                method: "GET",
                url: api.sdk.getFunctionInvocationChildren(invocationId),
                params: params
            });
        }

        getData();
        poll = $interval(function () {
            if (((new Date()) - lastPoll) > pollInterval) {
                lastPoll = new Date();
                getData();
            }
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
