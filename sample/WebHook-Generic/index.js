module.exports = function (req, context) {
    context.log('Webhook was triggered!');
    context.done(null, 'WebHook processed successfully! ' + req.body.a);
}