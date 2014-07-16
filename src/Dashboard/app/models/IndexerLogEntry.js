angular.module('dashboard').factory('IndexerLogEntry', function (stringUtils) {
    function IndexerLogEntry() {

    }

    IndexerLogEntry.fromJson = function (item) {
        var model = new IndexerLogEntry();

        model.id = item.id;
        model.date = item.date;
        model.title = item.title;
        model.exceptionDetails = item.exceptionDetails;

        return model;
    };

    return IndexerLogEntry;
});
