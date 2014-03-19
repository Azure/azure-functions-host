angular.module('dashboard').directive('consoleOutput', function () {
    return {
        restrict: 'E',
        scope: {
            collapseByDefault: '&',
            supportsIncrementalUpdates: '&',
            logUrl: '&',
            consoleOwner: '&'
        },
        controller: 'ConsoleOutputController',
        templateUrl: 'app/views/shared/ConsoleOutput.html'
    };
});