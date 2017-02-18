module.exports = function (context, res) {
    context.log('JavaScript HTTP trigger function processed a request.');

    res.headers['ResponseServerDateTime'] = '123';
	
	res.responseBody = 'testBody123';
    res.contentHeaders['Content-Type'] = 'text/html; charset=utf-8';

	res.statusCode = 404;
	res.reasonPhrase = 'Not Found';
	
	if(res.request.method == 'POST')
	{
		res.request.method = 'GET';
	}
    
	context.done(null, res);   
};