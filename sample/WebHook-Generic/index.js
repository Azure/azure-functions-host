module.exports = function (context, data) {
    context.log('Webhook was triggered!');
    context.res = {
        body: {
            result: 'Value: ' + data.value
        }
    };
    context.done();
}