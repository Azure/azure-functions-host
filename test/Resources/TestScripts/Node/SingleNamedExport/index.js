var util = require('util');

module.exports.myFunc = function (context) {
    var log = util.format("Exports: IsObject=%s, Count=%d",
        util.isObject(module.exports),
        Object.keys(module.exports).length);
    context.log(log);
    context.done();
}