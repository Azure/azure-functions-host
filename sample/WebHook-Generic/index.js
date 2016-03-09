module.exports = function (context, body) {
    context.log('Webhook was triggered!');
    context.res = {
        body: 'WebHook processed successfully! ' + body.a
    };
    context.done();
}