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
                id: 123,
                category: req.params.category
            },
            {
                id: 456,
                category: req.params.category
            }
        ];
    }

    context.done(null, result);
}
