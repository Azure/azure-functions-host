var util = require('util');

module.exports = function (context, input) {
    context.log('Node.js manually triggered function called with input', input);

    var blobIn = context.bindings.blobIn;
    context.log('First: %s Last:%s', blobIn.first, blobIn.last);

    context.done(null, util.format(blobIn.first, blobIn.last));
}