var logger = require('./logger');

// demonstrates that a function can be a node module, with the
// manifest pointing to a module directory rather than a single file
module.exports = function (context) {
    var timeStamp = new Date().toISOString();
    context.log('Node.js timer trigger function ran at ' + timeStamp);

    logger.log(timeStamp, context.done);
}