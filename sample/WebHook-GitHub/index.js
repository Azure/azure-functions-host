module.exports = function (body, context) {
    context.log('GitHub WebHook triggered! ' + body.comment.body);
    context.done(null, 'New GitHub comment: ' + body.comment.body);
}