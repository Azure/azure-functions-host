angular.module('dashboard').controller('FunctionsTableController',
    function FunctionsTableController($scope, $http, api, FunctionDefinition) {

        if (!$scope.functionDefinitions || !$scope.functionDefinitions.endpoint) {
            throw Error("Parent scope must define 'functionDefinitions' object, with an 'endpoint' property pointing at the server endpoint for retrieving Invocation objects.");
        }

        $scope.functionDefinitions.firstPage = true;
        $scope.functionDefinitions.initializing = true;
        $scope.functionDefinitions.newestInPageStack = [];
        $scope.functionDefinitions.nonFinalInvocations = {};
        $scope.functionDefinitions.loadPreviousPage = function () {
            loadPreviousPage();
        };
        $scope.functionDefinitions.loadNextPage = function () {
            loadNextPage();
        };
        $scope.functionDefinitions.loadFirstPage = function () {
            loadFirstPage();
        };

        $scope.$on('functionDefinitions:poll', function () {
            onPoll();
        });

        function onPoll() {
            if ($scope.functionDefinitions.initializing) {
                loadFirstPage();
                return;
            }
            if ($scope.functionDefinitions.firstPage && !$scope.functionDefinitions.hasNew) {
                hasNewFunctionDefinitions();
            }
        }

        function getFunctionDefinitions(params) {
            return $http.get($scope.functionDefinitions.endpoint, { params: params });
        }

        function hasNewFunctionDefinitions() {
            if (!$scope.functionDefinitions.entries) {
                return;
            }
            if ($scope.functionDefinitions.hasNew || !$scope.functionDefinitions.firstPage) {
                return;
            }
            if ($scope.functionDefinitions.skipHasNewPolling) {
                return;
            }
            var params = {
                limit: 1
            };
            if ($scope.functionDefinitions.entries.length !== 0) {
                params.newerThan = $scope.functionDefinitions.entries[0].key;
            }
            getFunctionDefinitions(params).success(function (data) {
                if (data.entries.length > 0) {
                    $scope.functionDefinitions.hasNew = true;
                } else {
                    $scope.functionDefinitions.hasNew = false;
                }
            });
        }

        function loadPage(params, sucess) {
            $scope.functionDefinitions.disablePager = true;
            return getFunctionDefinitions(params)
                .success(function() {
                    handleFunctionDefinitionsPage.apply(this, arguments);
                    if (typeof sucess === 'function') {
                        sucess.apply(this, arguments);
                    }
                })
                .error(handleFunctionDefinitionsPageError)
                ['finally'](function () {
                    $scope.functionDefinitions.disablePager = false;
                });
        }

        function handleFunctionDefinitionsPageError() {
        }

        function loadNextPage() {
            if ($scope.functionDefinitions.entries.length === 0) {
                return;
            }
            var oldestOnPage = $scope.functionDefinitions.entries[$scope.functionDefinitions.entries.length - 1].key;
            loadPage({ limit: 10, olderThan: oldestOnPage });
        }

        function loadPreviousPage() {
            // pop the current page's head out
            $scope.functionDefinitions.newestInPageStack.pop();
            // pop the previous page's head out
            var previous = $scope.functionDefinitions.newestInPageStack.pop();
            if (!previous) {
                return;
            }
            loadPage({ limit: 10, olderThanOrEqual: previous });
        }

        function loadFirstPage() {
            // empty the stack
            while ($scope.functionDefinitions.newestInPageStack.pop());
            loadPage({ limit: 10 }, function () {
                $scope.functionDefinitions.hasNew = false;
            })
                ['finally'](function () {
                    $scope.functionDefinitions.initializing = false;
                    $scope.functionDefinitions.hasNew = false;
                });
        }

        function handleFunctionDefinitionsPage(data) {
            var ix,
                item,
                functionDefinition,
                result = data.entries,
                len = result.length;

            $scope.storageAccount = data.storageAccountName;
            $scope.functionDefinitions.initializing = false;
            $scope.functionDefinitions.entries = [];

            if (len > 0) {
                for (ix = 0; ix !== len; ++ix) {
                    item = result[ix];
                    functionDefinition = FunctionDefinition.fromJson(item);
                    $scope.functionDefinitions.entries.push({ functionDefinition: functionDefinition, key: functionDefinition.functionId });
                }
                $scope.functionDefinitions.newestInPageStack.push($scope.functionDefinitions.entries[0].key);
                $scope.functionDefinitions.firstPage = $scope.functionDefinitions.newestInPageStack.length === 1;
            }
            $scope.functionDefinitions.hasMore = data.hasMore;
        }
    }
);