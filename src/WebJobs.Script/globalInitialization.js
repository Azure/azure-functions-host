// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

var process = require('process');

return function (context, callback) {{
    process.on('uncaughtException', function (err) {{
        context.handleUncaughtException(err.stack);
    }});

    callback();
}};

