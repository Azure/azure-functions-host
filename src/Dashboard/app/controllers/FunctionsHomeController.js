angular.module('dashboard').controller('FunctionsHomeController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionDefinition, FunctionInvocationSummary, api, isUsingSdk) {
        var poll,
            functionsPollInterval = 20 * 1000,
            invocationsPollInterval = 10 * 1000,
            lastFunctionsPoll = 0,
            lastInvocationsPoll = 0;

        isUsingSdk.findOut($scope);

        $scope.breadcrumbs = [];

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.invocations = {
            endpoint: api.sdk.recentInvocations()
        };

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
                    $scope.functionDefinitions.push(functionDefinition);
                }
            });
        }

        poll = $interval(function () {
            if (((new Date()) - lastFunctionsPoll) > functionsPollInterval) {
                lastFunctionsPoll = new Date();
                getFunctionDefinitions();
            }
            if (((new Date()) - lastInvocationsPoll) > invocationsPollInterval) {
                lastInvocationsPoll = new Date();
                $scope.$broadcast('invocations:poll');
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
