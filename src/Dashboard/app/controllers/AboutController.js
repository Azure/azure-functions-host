angular.module('dashboard').controller('AboutController',
    function ($scope, $routeParams, $interval, $http, api) {
        $scope.indexerLogs = {
            endpoint: api.sdk.indexerLogs()
        };
    }
);
