angular.module('dashboard').controller('ConsoleOutputController',
    function($scope, $routeParams, $interval, $http, api) {
        var consoleLoadedAtLeastOne = false;
        var emptyText = "Loading ...";
        
        $scope.toggleConsole = function () {
            $scope.shouldShowConsole = !$scope.shouldShowConsole;
            if ($scope.shouldShowConsole) {
                if (!$scope.consoleText || $scope.consoleText.length === 0) {
                    if (!consoleLoadedAtLeastOne) {
                        $scope.consoleText = emptyText;
                    }
                    loadConsole();
                }
            }
        };

        function loadConsole() {
            if (!$scope.isRunning() && consoleLoadedAtLeastOne) {
                return;
            }

            if (!$scope.shouldShowConsole) {
                return;
            }
            var start = 0;
            if ($scope.supportsIncrementalUpdates && $scope.consoleText && $scope.consoleText.length > 0) {
                start = $scope.consoleText.split(/\n/).length;
            }
            $http({
                method: "GET",
                url: $scope.logUrl(),
                params: { start: start }
            }).then(function (res) {
                consoleLoadedAtLeastOne = true;
                if (res.data.length === 0) {
                    return;
                }
                if ($scope.supportsIncrementalUpdates && $scope.consoleText && $scope.consoleText.length > 0 && $scope.consoleText !== emptyText) {
                    $scope.consoleText = $scope.consoleText + '\n' + res.data;
                } else {
                    $scope.consoleText = res.data;
                }
            });
        }
    }
);