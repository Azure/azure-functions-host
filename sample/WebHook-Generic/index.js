module.exports = function (context) {
    context.log('WebHook request triggered!');
    context.done(null, 'WebHook processed successfully :)');
}