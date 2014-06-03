angular.module('dashboard').controller('FunctionsHomeController',
    function ($scope, $routeParams, $interval, $http, stringUtils, api, isUsingSdk) {
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
        $scope.functionDefinitions = {
            endpoint: api.sdk.functionDefinitions()
        };

        poll = $interval(function () {
            if (((new Date()) - lastFunctionsPoll) > functionsPollInterval) {
                lastFunctionsPoll = new Date();
                $scope.$broadcast('functionDefinitions:poll');
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
