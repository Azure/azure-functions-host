module.exports = function (context) {
    context.done(null, 'WebHook processed successfully! ' + context.req.body.a);
}