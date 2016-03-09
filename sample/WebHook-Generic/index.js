module.exports = function (context, data) {
    context.log('Webhook was triggered!');
    context.res = {
        body: 'WebHook processed successfully! ' + data.a
    };
    context.done();
}