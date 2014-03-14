angular.module('dashboard').controller('FunctionsHomeController',
    function ($scope, $routeParams, $interval, $http, stringUtils, FunctionDefinition, FunctionInvocationSummary, api, urls) {
        var poll,
            pollInterval = 10 * 1000,
            lastPoll = 0;

        if ($scope._sdkNotConfigured) {
            return;
        }

        $scope.breadcrumbs = [];

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

        function getData() {
            lastPoll = new Date();
            getFunctionDefinitions();
            $scope.$broadcast('invocations:poll');
        }

        poll = $interval(function () {
            if (((new Date()) - lastPoll) > pollInterval) {
                getData();
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
