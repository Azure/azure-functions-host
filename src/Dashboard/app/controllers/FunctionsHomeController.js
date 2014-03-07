angular.module('dashboard').controller('FunctionsHomeController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionDefinition, FunctionInvocationSummary, api, urls) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0;

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.breadcrumbs = [];

        function getFunctionDefinitions() {
            return $http.get(api.sdk.functionDefinitions()).then(function (res) {
                var ix,
                    item,
                    functionDefinition,
                    result = res.data.functionStatisticsViewModels,
                    len = result.length;
                $scope.storageAccount = res.data.storageAccountName;
                $scope.functionDefinitions = [];
                for (ix = 0; ix !== len; ++ix) {
                    item = result[ix];
                    functionDefinition = FunctionDefinition.fromJson(item);
                    functionDefinition.rowKeyForJobRunLookup = item.rowKey;
                    $scope.functionDefinitions.push(functionDefinition);
                }
            });
        }

        function getFunctionInvocations() {
            return $http.get(api.sdk.recentInvocations()).then(function (res) {
                var len = res.data.length,
                    ix,
                    item,
                    invocation;
                $scope.invocations = [];
                for (ix = 0; ix !== len; ++ix) {
                    item = res.data[ix];
                    invocation = FunctionInvocationSummary.fromJson(item);
                    invocation.rowKeyForJobRunLookup = item.rowKey;
                    $scope.invocations.push(invocation);
                }
            });
        }

        function getData() {
            getFunctionDefinitions();
            getFunctionInvocations();
        }

        function startPolling() {
            poll = $interval(function () {
                if (((new Date()) - lastPoll) > pollInterval) {
                    lastPoll = new Date();
                    getData();
                }
            }, 2000);
        }

        startPolling();
        $scope.$on('$destroy', function () {
            // Make sure that the interval is destroyed too
            if (poll) {
                $interval.cancel(poll);
                poll = undefined;
            }
        });
    }
);
