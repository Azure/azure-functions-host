angular.module('dashboard').controller('InvocationsTableController',
    function InvocationsTableController($scope, $http, api, FunctionInvocationSummary) {

        if (!$scope.invocations || !$scope.invocations.endpoint) {
            throw Error("Parent scope must define 'invocations' object, with an 'endpoint' property pointing at the server endpoint for retrieving Invocation objects.");
        }

        $scope.invocations.firstPage = true;
        $scope.invocations.initializing = true;
        $scope.invocations.newestInPageStack = [];
        $scope.invocations.nonFinalInvocations = {};
        $scope.invocations.loadPreviousPage = function () {
            loadPreviousPage();
        };
        $scope.invocations.loadNextPage = function () {
            loadNextPage();
        };
        $scope.invocations.loadFirstPage = function () {
            loadFirstPage();
        };

        $scope.$on('invocations:poll', function () {
            onPoll();
        });

        $scope.$on('invocations:updateTiming', function () {
            onUpdateTiming();
        });

        function onPoll() {
            if ($scope.invocations.initializing) {
                loadFirstPage();
                return;
            }
            if ($scope.invocations.firstPage && !$scope.invocations.hasNew) {
                hasNewInvocations();
            }
            updateNonFinalInvocations();
        }

        function onUpdateTiming() {
            if (!$scope.invocations.entries) {
                return;
            }
            var ix, len = $scope.invocations.entries.length;
            for (ix = 0; ix !== len; ++ix) {
                $scope.invocations.entries[ix].invocation.updateTimingStrings();
            }
        }

        function getFunctionInvocations(params) {
            return $http.get($scope.invocations.endpoint, { params: params });
        }

        function hasNewInvocations() {
            if ($scope.invocations.hasNew || !$scope.invocations.firstPage) {
                return;
            }
            if ($scope.invocations.skipHasNewPolling) {
                return;
            }
            getFunctionInvocations({ newerThan: $scope.invocations.entries[0].key, limit: 1 }).success(function (data) {
                if (data.entries.length > 0) {
                    $scope.invocations.hasNew = true;
                } else {
                    $scope.invocations.hasNew = false;
                }
            });
        }

        function loadPage(params, sucess) {
            $scope.invocations.disablePager = true;
            return getFunctionInvocations(params)
                .success(function() {
                    handleInvocationsPage.apply(this, arguments);
                    if (typeof sucess === 'function') {
                        sucess.apply(this, arguments);
                    }
                })
                .error(handleInvocationsPageError)
                ['finally'](function () {
                    console.log("loagPage.finally");
                    $scope.invocations.disablePager = false;
                });
        }

        function updateNonFinalInvocations() {
            var nonFinalIds = Object.getOwnPropertyNames($scope.invocations.nonFinalInvocations);
            if (nonFinalIds.length === 0) {
                return;
            }
            $http.post(api.sdk.invocationByIds(), JSON.stringify(nonFinalIds)).success(function (data) {
                var len = data.length,
                    ix,
                    item,
                    invocation;
                for (ix = 0; ix !== len; ++ix) {
                    item = data[ix];
                    invocation = $scope.invocations.nonFinalInvocations[item.id];
                    if (invocation) {
                        invocation.functionDisplayTitle = item.functionDisplayTitle;
                        invocation.status = item.status;
                        invocation.when = item.whenUtc;
                        invocation.duration = item.duration;
                        invocation.exceptionMessage = item.exceptionMessage;
                        invocation.updateTimingStrings();
                        if (invocation.isFinal()) {
                            delete $scope.invocations.nonFinalInvocations[invocation.id];
                        }
                    }
                }
            });
        }

        function handleInvocationsPageError() {
        }

        function loadNextPage() {
            if ($scope.invocations.entries.length === 0) {
                return;
            }
            var oldestOnPage = $scope.invocations.entries[$scope.invocations.entries.length - 1].key;
            loadPage({ limit: 10, olderThan: oldestOnPage });
        }

        function loadPreviousPage() {
            // pop the current page's head out
            $scope.invocations.newestInPageStack.pop();
            // pop the previous page's head out
            var previous = $scope.invocations.newestInPageStack.pop();
            if (!previous) {
                return;
            }
            loadPage({ limit: 10, olderThanOrEqual: previous });
        }

        function loadFirstPage() {
            // empty the stack
            while ($scope.invocations.newestInPageStack.pop());
            loadPage({ limit: 10 }, function () {
                    $scope.invocations.hasNew = false;
                    console.log("loagFirstPage.success");
                })
                ['finally'](function (a,b,c,d) {
                    console.log("loagFirstPage.finally");
                    $scope.invocations.initializing = false;
                    $scope.invocations.hasNew = false;
                });
        }

        function handleInvocationsPage(data) {
            var len = data.entries.length,
                ix,
                item,
                invocation;
            $scope.invocations.initializing = false;
            $scope.invocations.entries = [];
            $scope.invocations.nonFinalInvocations = {};
            if (len > 0) {
                for (ix = 0; ix !== len; ++ix) {
                    item = data.entries[ix];
                    invocation = FunctionInvocationSummary.fromJson(item.invocation);
                    if (!invocation.isFinal()) {
                        $scope.invocations.nonFinalInvocations[invocation.id] = invocation;
                    }
                    $scope.invocations.entries.push({ invocation: invocation, key: item.key });
                }
                $scope.invocations.newestInPageStack.push($scope.invocations.entries[0].key);
                $scope.invocations.firstPage = $scope.invocations.newestInPageStack.length === 1;
            }
            $scope.invocations.hasMore = data.hasMore;
        }

    }
);