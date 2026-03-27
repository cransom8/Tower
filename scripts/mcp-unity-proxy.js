#!/usr/bin/env node
// Proxy for MCP for Unity HTTP server.
// Claude Code does OAuth discovery and fails when the server returns plain-text 404s.
// This proxy returns valid JSON {} for OAuth endpoints so Claude Code falls through
// to unauthenticated mode, then forwards all other traffic to the Unity MCP server.

const http = require('http');

const UNITY_PORT = 8080;
const PROXY_PORT = 8082;

const OAUTH_PATHS = new Set([
  '/.well-known/oauth-authorization-server',
  '/.well-known/openid-configuration',
  '/.well-known/oauth-protected-resource',
  '/register',
]);

const server = http.createServer((req, res) => {
  console.log(`[proxy] ${req.method} ${req.url}`);
  if (OAUTH_PATHS.has(req.url)) {
    console.log(`[proxy] intercepted OAuth path, returning {}`);
    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end('{}');
    return;
  }

  const options = {
    hostname: 'localhost',
    port: UNITY_PORT,
    path: req.url,
    method: req.method,
    headers: { ...req.headers, host: `localhost:${UNITY_PORT}` },
  };

  const proxyReq = http.request(options, (proxyRes) => {
    res.writeHead(proxyRes.statusCode, proxyRes.headers);
    proxyRes.pipe(res);
  });

  proxyReq.on('error', () => {
    if (!res.headersSent) {
      res.writeHead(502, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: 'Unity MCP server unavailable' }));
    }
  });

  req.pipe(proxyReq);
});

server.listen(PROXY_PORT, '127.0.0.1', () => {
  console.log(`MCP Unity proxy running on http://localhost:${PROXY_PORT}/mcp`);
  console.log(`Forwarding to http://localhost:${UNITY_PORT}/mcp`);
});
