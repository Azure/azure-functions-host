module.exports = function (context, input) {
    context.log('Sending native windows toast notification...');
    context.bindings.wnsToastPayload = "<toast><visual><binding template=\"ToastText01\"><text id=\"1\">Test message from Node!</text></binding></visual></toast>";
    context.done();
}