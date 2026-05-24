// Same as with-start-script/server.js. Operator pins the launch command
// manually because package.json has no `scripts.start`.
'use strict';

const http = require('http');

const portRaw = process.env.PORT;
const port = Number.parseInt(portRaw, 10);
if (!Number.isInteger(port) || port <= 0) {
  console.error(`[uat-fixture] invalid PORT: ${portRaw}`);
  process.exit(2);
}

const server = http.createServer((req, res) => {
  if (req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    res.end('ok');
    return;
  }
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end(`UAT nodejs-app fixture (no-start-script) - PORT=${port}`);
});

server.listen(port, () => {
  console.log(`[uat-fixture] listening on PORT=${port}`);
});
