angular.module('dashboard').controller('FunctionController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionInvocationSummary, api, urls) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0,
            functionName = $routeParams.functionFullName;

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.functionFullName = functionName;
        $scope.nonFinalInvocations = {};

        $scope.breadcrumbs = [];

        function getFunctionInvocations() {
            return $http.get(api.sdk.invocationsByFunction(functionName)).then(function (res) {
                var len = res.data.length,
                    ix,
                    item,
                    invocation;
                $scope.invocations = [];
                for (ix = 0; ix !== len; ++ix) {
                    item = res.data[ix];
                    invocation = FunctionInvocationSummary.fromJson(item.invocation);
                    invocation.rowKeyForJobRunLookup = item.rowKey;
                    $scope.invocations.push(invocation);
                }
            });
        }

        function getData() {
            getFunctionInvocations();
        }

        function updateTiming() {
            if (!$scope.invocations) {
                return;
            }
            var ix, len = $scope.invocations.length;
            for (ix = 0; ix !== len; ++ix) {
                $scope.invocations[ix].updateTimingStrings();
            }
        }

        function startPolling() {
            poll = $interval(function () {
                if (((new Date()) - lastPoll) > pollInterval) {
                    lastPoll = new Date();
                    getData();
                }
                updateTiming();
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
