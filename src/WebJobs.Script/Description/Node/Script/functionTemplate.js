// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

var f = require('{0}'),
    util = require('util');

return function (context, callback) {{
    // TEMP HACK: workaround for https://github.com/tjanczuk/edge/issues/325
    setImmediate(() => {{}});

    f = getEntryPoint(f, context);

    var origLog = context.log;
    context.log = function() {{
        value = util.format.apply(null, arguments);
        origLog(value);
    }};

    context.done = function(err, result) {{
        if (context._done) {{
            context.log("Error: 'done' has already been called. Please check your script for extraneous calls to 'done'.");
            return;
        }}
        context._done = true;

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

    var inputs = context._inputs;
    inputs.unshift(context);
    delete context._inputs;

    f.apply(null, inputs);
}};

function getEntryPoint(f, context) {{
    if (util.isObject(f)) {{
        if (context._entryPoint) {{
            // the module exports multiple functions
            // and an explicit entry point was named
            f = f[context._entryPoint];
            delete context._entryPoint;
        }}
        else if (Object.keys(f).length == 1) {{
            // a single named function was exported
            f = f[Object.keys(f)[0]];
        }}
        else {{
            // finally, see if there is an exported function named
            // 'run' by convention
            f = f['run'];
        }}
    }}
    else if (!util.isFunction(f)) {{
        // the module must export an object or a function
        f = null;
    }}

    if (!f) {{
        throw "Unable to determine function entry point. If multiple functions are exported, " +
              "you must indicate the entry point, either by naming it 'run', or by naming it " +
              "explicitly via the 'entryPoint' metadata property.";
    }}

    return f;
}}