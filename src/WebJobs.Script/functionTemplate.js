// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

var f = require('{0}');

return function (context, callback) {{
    context.done = callback;

    if (context.hasOwnProperty('input')) {{
        var input = context.input;
        delete context.input;
        f(input, context);
    }}
    else {{
        f(context);
    }}
}};