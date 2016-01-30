module.exports = function (body, context) {
    context.log('Webhook was triggered!');
    context.done(null, 'WebHook processed successfully! ' + body.a);
}