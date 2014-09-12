angular.module('dashboard').controller('IndexerProgressController', function ($scope, $http, $q, $interval, api) {
    var // Slow update when there is no indexing happening
        NO_INDEXING_POOL_INTERVAL = 30 * 1000,
        // Fast update when there is some indexing happening
        INDEXING_POOL_INTERVAL = 5 * 1000,
        // The minimum number of items in the indexingin queue
        // required to show the notification
        SIGNIFICANT_QUEUE_LENGTH_LIMIT = 50;

    var pool,
        currentPoolInterval = 0,
        lastInvocationsPoll = 0,
        updateInProgress = false;

    $scope._indexerProgress = {};

    function updateIndexingStatus(callback) {
        isUpgrading(function (upgrading) {
            setUpgradingFlag(upgrading);
            if (!upgrading) {
                checkIndexingQueueLengthIsSignificant(function (isSignificant) {
                    setIndexingFlag(isSignificant);
                    if (isSignificant) {
                        getRemainingItemsToIndex(null, function (remainingItems) {
                            setRemainingItems(remainingItems);
                        });
                    }

                    callback(isSignificant)
                });
            }
        })
    }

    function checkIndexingQueueLengthIsSignificant(callback) {
        return getRemainingItemsToIndex(
            SIGNIFICANT_QUEUE_LENGTH_LIMIT,
            function (remainingItems) {
                // Display the notification if there are more items than the limit
                // or if the notification is already visible and we haven't reached zero
                var isSignificant =
                    remainingItems >= SIGNIFICANT_QUEUE_LENGTH_LIMIT ||
                    ($scope._indexerProgressShow && remainingItems > 0);
                callback(isSignificant);
            }
        );
    }

    function getRemainingItemsToIndex(limit, callback) {
        return $http.get(api.sdk.indexingQueueLength(limit))
            .then(function (res) {
                if (res.status == 200 && !isNaN(res.data)) {
                    callback(parseInt(res.data, 10));
                }
            });
    }

    function isUpgrading(callback) {
        var dashboardUpgradeState = {
            finished: 2
        };

        return $http.get(api.sdk.upgrading())
            .then(function (res) {
                callback(res.data.upgradeState !== dashboardUpgradeState.finished);
            });
    }

    function setIndexingFlag(displayProgress) {
        if ($scope._indexerProgressShow === displayProgress) {
            return;
        }

        setRemainingItems(undefined);
        $scope._indexerProgressShow = displayProgress;
    }

    function setUpgradingFlag(displayProgress) {
        $scope._indexerProgressShow = displayProgress;
    }

    function setRemainingItems(items) {
        $scope._indexerProgressRemainingItems = items;
    }

    $scope.$on('$destroy', function () {
        // Make sure that the interval is destroyed too
        if (poll) {
            $interval.cancel(poll);
            poll = undefined;
        }
    });

    setIndexingFlag(false);

    poll = $interval(function () {
        if ((new Date() - lastInvocationsPoll) > currentPoolInterval && !updateInProgress) {
            // This flag is used because updateIndexingStatus is async and we don't want to run
            // multiple queries at the same time
            updateInProgress = true;

            updateIndexingStatus(function (isIndexing) {
                currentPoolInterval = isIndexing ? INDEXING_POOL_INTERVAL : NO_INDEXING_POOL_INTERVAL;
                updateInProgress = false;
                lastInvocationsPoll = new Date();
            });
        }
    },
    INDEXING_POOL_INTERVAL);
});
