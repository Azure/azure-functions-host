var util = require('util');

module.exports = {
    doIt: doIt,
    foo: foo,
    bar: bar,
    baz: function () {
    }
}

function doIt(context) {
    var log = util.format("Exports: IsObject=%s, Count=%d",
        util.isObject(module.exports),
        Object.keys(module.exports).length);
    context.log(log);
    context.done();
}

function foo() {
}

function bar() {
}