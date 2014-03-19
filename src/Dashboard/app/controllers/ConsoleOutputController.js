angular.module('dashboard').controller('ConsoleOutputController',
    function ($scope, $element, $timeout, $http, stringUtils) {
        var consoleLoadedAtLeastOnce = false,
            lastUpdated = null,
            textArea = $element.find('textarea');

        $scope.shouldShowConsole = !$scope.collapseByDefault();
        $scope.toggleConsole = function () {
            $scope.shouldShowConsole = !$scope.shouldShowConsole;
            if ($scope.shouldShowConsole) {
                loadConsole();
            }
        };

        $scope.isRunning = function () {
            return $scope.consoleOwner().isRunning();
        };

        $scope.lastUpdatedText = function () {
            if (!lastUpdated) {
                return null;
            }
            return stringUtils.formatDateTime(lastUpdated);
        };

        $scope.refresh = loadConsole;

        function loadConsole() {
            if (!$scope.isRunning() && consoleLoadedAtLeastOnce) {
                return;
            }

            if (!$scope.shouldShowConsole) {
                return;
            }
            var start = 0, originalText = null;
            if ($scope.supportsIncrementalUpdates() && $scope.consoleText && $scope.consoleText.length > 0) {
                originalText = $scope.consoleText;
                start = originalText.split(/\n/).length;
            }
            $http({
                method: "GET",
                url: $scope.logUrl(),
                params: { start: start }
            }).then(function (res) {
                consoleLoadedAtLeastOnce = true;
                if (res.data.length === 0) {
                    return;
                }
                if (originalText !== null) {
                    $scope.consoleText = originalText + '\n' + res.data;
                } else {
                    $scope.consoleText = res.data;
                }
                // TODO: do this in a more angular-y way - do not  manipulate DOM directly from a controller
                $timeout(function() {
                    textArea.scrollTop(textArea[0].scrollHeight);
                });
                lastUpdated = new Date();
            });
        }

        if ($scope.shouldShowConsole) {
            loadConsole();
        }
    }
);