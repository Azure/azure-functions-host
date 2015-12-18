var util = require('util');

module.exports = function (context) {
    context.log('Node.js queue trigger function processed work item ' + context.workItem.id);

    context.output({
        receipt: JSON.stringify(context.workItem)
    });

    context.done();
}