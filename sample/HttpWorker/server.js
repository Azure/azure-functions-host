var http = require('http');
const url = require('url');
const port = process.env.FUNCTIONS_HTTPWORKER_PORT;
console.log("port" + port);
//create a server object:
http.createServer(function (req, res) {
  const reqUrl = url.parse(req.url, true);
  console.log("Request handler random was called.");
  res.writeHead(200, {"Content-Type": "application/json"});
  var json = JSON.stringify({ functionName : req.url.replace("/","")});
  res.end(json);
}).listen(port); 