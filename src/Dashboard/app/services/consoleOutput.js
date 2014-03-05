angular.module('dashboard').directive('consoleOutput', function () {
    return {
        restrict: 'E',
        scope: {
            logUrl: '&',
            supportsIncrementalUpdates: '@',
            isRunning: '&'
        },
        controller: 'ConsoleOutputController',
        templateUrl: 'app/views/shared/ConsoleOutput.html'
    };
});