;
(function() {
    var app = angular.module('dashboardApp', [
        'ngRoute',
        'dashboard'
    ]);

    var dashboard = angular.module('dashboard', []);

    dashboard.run(function($rootScope, invocationUtils, stringUtils, api, urls) {
        $rootScope.invocationUtils = invocationUtils;
        $rootScope.stringUtils = stringUtils;
        $rootScope._api = api;
        $rootScope._urls = urls;
    });

    // this is a basis for some perf improvements
    // for things that only needs to bind, well, once. 
    app.directive('bindOnce', function () {
        return {
            scope: true,
            link: function($scope, $element) {
                setTimeout(function () {
                    $scope.$destroy();
                    $element.removeClass('ng-binding ng-scope');
                }, 0);
            }
        };
    });

    dashboard.factory('$exceptionHandler', function() {
        return function(exception, cause) {
            exception.message += ' (caused by "' + cause + '")';
            console.log(["CATCH", exception, cause]);
            throw exception;
        };
    });

    app.config(['$routeProvider',
        function ($routeProvider) {
            var defaultHomePage = '/jobs'; //or /functions if not in antares
            $routeProvider.
                when('/', {
                    redirectTo: defaultHomePage
                }).
                when('/jobs', {
                    templateUrl: 'app/views/JobsList.html',
                    controller: 'JobsListController'
                }).
                when('/jobs/triggered/:jobName', {
                    templateUrl: 'app/views/TriggeredJob.html',
                    controller: 'TriggeredJobController'
                }).
                when('/jobs/continuous/:jobName', {
                    templateUrl: 'app/views/ContinuousJob.html',
                    controller: 'ContinuousJobController'
                }).
                when('/jobs/triggered/:jobName/runs/:runId', {
                    templateUrl: 'app/views/TriggeredJobRun.html',
                    controller: 'TriggeredJobRunController'
                }).
                when('/functions', {
                    templateUrl: 'app/views/FunctionsHome.html',
                    controller: 'FunctionsHomeController'
                }).
                when('/functions/definitions/:functionName', {
                    templateUrl: 'app/views/Function.html',
                    controller: 'FunctionController'
                }).
                when('/functions/invocations/:invocationId', {
                    templateUrl: 'app/views/FunctionInvocation.html',
                    controller: 'FunctionInvocationController'
                }).
                otherwise({
                    redirectTo: '/'
                });
        }]);

    // simple paging support
    app.filter('startFrom', function() {
        return function(input, start) {
            start = +start; // ensure int
            return input.slice(start);
        };
    });
})();