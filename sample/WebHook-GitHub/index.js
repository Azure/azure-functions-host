module.exports = function (context) {
    context.log('GitHub WebHook triggered!');
    context.done(null, 'GitHub WebHook processed successfully :)');
}