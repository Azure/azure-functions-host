module.exports = function (req, context) {
    context.log('GitHub WebHook triggered! ' + req.body.comment.body);
    context.done(null, 'New GitHub comment: ' + req.body.comment.body);
}