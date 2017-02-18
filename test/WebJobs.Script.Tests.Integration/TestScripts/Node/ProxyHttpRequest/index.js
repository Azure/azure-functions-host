module.exports = function (context, req) {
    context.log('JavaScript HTTP trigger function processed a request.');

    req.headers['ServerDateTime'] = '123';
   
	req.method = "GET";
	
	req.originalUrl = "http://www.bing.com";
	
	req.body = 'testBody';
    req.contentHeaders['Content-Type'] = 'text/html; charset=utf-8';
		
    context.done(null, req);
};