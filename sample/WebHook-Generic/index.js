module.exports = function (context) {
    context.log('Webhook was triggered!');
    context.done(null, 'WebHook processed successfully! ' + context.req.body.a);
}