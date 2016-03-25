module.exports = function (context, input) {
    context.log('Node.js manually triggered function called with input', input);

    var status = context.bindings.status;
    context.log('Status: Level:%d Detail:%s', status.level, status.detail);

    context.bindings.result = status.detail;

    context.done();
}