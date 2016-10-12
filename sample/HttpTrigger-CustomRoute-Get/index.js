var util = require('util');

module.exports = function (context, req) {
    context.log('Node.js HTTP trigger function processed a request. Name=%s', req.query.name);

    context.log('category: %s id: %s', context.bindingData.category, context.bindingData.id);

    if (req.params.id) {
        // single product lookup
        result = {
            id: req.params.id,
            category: req.params.category
        };
    } else {
        // multiple products
        result = [
            {
                id: '12ec126e-3208-4542-a4a0-30e65438832a',
                category: req.params.category
            },
            {
                id: '4e2796ae-b865-4071-8a20-2a15cbaf856c',
                category: req.params.category
            }
        ];
    }

    context.done(null, result);
}
