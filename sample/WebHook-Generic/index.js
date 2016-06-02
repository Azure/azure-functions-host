module.exports = function (context, payload) {
    context.log('Webhook was triggered!');
    context.res = {
        body: {
            result: 'Value: ' + payload.value
        }
    };
    context.done();
}