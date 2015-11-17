var util = require('util');

module.exports = function (context) {
    var workItem = context.input,
        options = {
            path: 'samples-workitems/' + workItem.ID,
            data: JSON.stringify(workItem)
        };

    context.blob.write(options, function () {
        context.log('Node.js queue trigger function processed work item ' + util.inspect(workItem.ID));
        context.done();
    });
}