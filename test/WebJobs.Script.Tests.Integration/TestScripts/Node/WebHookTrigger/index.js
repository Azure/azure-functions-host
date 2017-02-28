module.exports = function (context, payload) {
    context.log('Webhook was triggered!');
    if (payload) {
        context.res.send('WebHook processed successfully! ' + payload.a);
    } else {
        context.res.send('No content');
    }
}