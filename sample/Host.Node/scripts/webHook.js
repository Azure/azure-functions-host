var util = require('util');

module.exports = function (context) {
    // expect the request body to be json
    var json = util.inspect(context.input);

    context.log('Node.js WebHook function invoked! ' + json);
    context.done();
}