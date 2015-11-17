var f = require('{0}');

return function (context, callback) {{
	context.done = callback;
	f(context);
}};