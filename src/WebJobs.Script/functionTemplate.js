﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

var f = require('{0}'),
    util = require('util');

return function (context, callback) {{
    var origLog = context.log;
    context.log = function(value) {{
        origLog(util.inspect(value));
    }};

    context.done = function(err, result) {{
        if (err) {{
            callback(err);
        }}
        else {{
            var values = {{}};
            if (context.res) {{
                context.bindings.res = context.res;
            }}
            for (var name in context.bindings) {{
                values[name] = context.bindings[name];
            }}
            context.bind(values, function(err) {{
                callback(err, result);
            }});
        }}
    }};

    var inputs = context.inputs;
    inputs.unshift(context);
    delete context.inputs;

    f.apply(null, inputs);
}};