module.exports = function (context, payload) {
    context.log('Webhook was triggered!');
    context.res = {
        body: 'WebHook processed successfully! ' + payload.a
    };
    context.done();
}