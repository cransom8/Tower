#!/usr/bin/env node
'use strict';

const dotenv = require('dotenv');
dotenv.config({ path: '.env.local' });
dotenv.config();

const http = require('http');
const crypto = require('crypto');
const { OAuth2Client } = require('google-auth-library');

const SCOPE = 'https://www.googleapis.com/auth/androidpublisher';

function parseArgs(argv) {
  const args = {};

  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (!token.startsWith('--')) {
      throw new Error(`Unexpected argument: ${token}`);
    }

    const key = token.slice(2);
    const next = argv[i + 1];
    if (!next || next.startsWith('--')) {
      args[key] = 'true';
      continue;
    }

    args[key] = next;
    i += 1;
  }

  return args;
}

function getOption(args, key, envKey) {
  return args[key] ?? process.env[envKey];
}

function printUsage() {
  console.log(`
Usage:
  node scripts/get-google-play-oauth-token.js [options]

Required:
  --client-id      or GOOGLE_PLAY_OAUTH_CLIENT_ID
  --client-secret  or GOOGLE_PLAY_OAUTH_CLIENT_SECRET

  Optional:
  --host           default localhost
  --port           default 53682
  --login-hint     Google account email to preselect
  --no-prompt      omit prompt=consent
  --help

This script starts a local callback server on localhost by default and prints an
authorization URL for a Desktop app OAuth client. After you approve access,
it exchanges the code and prints the refresh token and env vars to use.
`.trim());
}

function htmlPage(title, body) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>${title}</title>
  <style>
    body { font-family: Arial, sans-serif; max-width: 720px; margin: 48px auto; line-height: 1.5; color: #111; }
    code { background: #f4f4f4; padding: 2px 4px; }
  </style>
</head>
<body>
  <h1>${title}</h1>
  ${body}
</body>
</html>`;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    printUsage();
    return;
  }

  const clientId = getOption(args, 'client-id', 'GOOGLE_PLAY_OAUTH_CLIENT_ID');
  const clientSecret = getOption(args, 'client-secret', 'GOOGLE_PLAY_OAUTH_CLIENT_SECRET');
  const host = getOption(args, 'host', 'GOOGLE_PLAY_OAUTH_HOST') || 'localhost';
  const port = Number(getOption(args, 'port', 'GOOGLE_PLAY_OAUTH_PORT') || 53682);
  const loginHint = getOption(args, 'login-hint', 'GOOGLE_PLAY_OAUTH_LOGIN_HINT');
  const usePrompt = !args['no-prompt'];

  if (!clientId || !clientSecret) {
    throw new Error('Missing client ID or client secret.');
  }
  if (!Number.isInteger(port) || port <= 0 || port > 65535) {
    throw new Error('Port must be an integer from 1 to 65535.');
  }
  if (!/^[a-zA-Z0-9.\-]+$/.test(host)) {
    throw new Error('Host must be a simple hostname or IP address.');
  }

  const redirectUri = `http://${host}:${port}/oauth2callback`;
  const oauthClient = new OAuth2Client(clientId, clientSecret, redirectUri);
  const state = crypto.randomBytes(24).toString('hex');

  const authorizationUrl = oauthClient.generateAuthUrl({
    access_type: 'offline',
    include_granted_scopes: true,
    scope: [SCOPE],
    state,
    prompt: usePrompt ? 'consent' : undefined,
    login_hint: loginHint || undefined,
  });

  const tokenPromise = new Promise((resolve, reject) => {
    const server = http.createServer(async (req, res) => {
      try {
        const requestUrl = new URL(req.url, `http://${host}:${port}`);

        if (requestUrl.pathname !== '/oauth2callback') {
          res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
          res.end('Not found.');
          return;
        }

        const returnedState = requestUrl.searchParams.get('state');
        const code = requestUrl.searchParams.get('code');
        const error = requestUrl.searchParams.get('error');

        if (error) {
          res.writeHead(400, { 'Content-Type': 'text/html; charset=utf-8' });
          res.end(htmlPage('Authorization Failed', `<p>Google returned <code>${error}</code>.</p>`));
          server.close();
          reject(new Error(`Authorization failed: ${error}`));
          return;
        }

        if (!code || returnedState !== state) {
          res.writeHead(400, { 'Content-Type': 'text/html; charset=utf-8' });
          res.end(htmlPage('Authorization Failed', '<p>Missing code or state mismatch.</p>'));
          server.close();
          reject(new Error('Missing code or state mismatch.'));
          return;
        }

        const { tokens } = await oauthClient.getToken(code);

        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(htmlPage(
          'Authorization Complete',
          '<p>You can close this window and return to the terminal.</p>'
        ));

        server.close();
        resolve(tokens);
      } catch (error) {
        server.close();
        reject(error);
      }
    });

    server.listen(port, host, () => {
      console.log(`Listening for Google OAuth callback on ${redirectUri}`);
      console.log('');
      console.log('Open this URL in the browser while signed into the Google account you want to use:');
      console.log(authorizationUrl);
      console.log('');
      console.log('If Google says the redirect URI is invalid, add this exact URI to the OAuth client:');
      console.log(redirectUri);
      console.log('');
    });

    server.on('error', reject);
  });

  const tokens = await tokenPromise;

  if (!tokens.refresh_token) {
    throw new Error(
      'Google did not return a refresh token. Revoke the app or re-run with prompt=consent and a fresh authorization.'
    );
  }

  console.log('Refresh token acquired.');
  console.log('');
  console.log(`GOOGLE_PLAY_OAUTH_CLIENT_ID=${clientId}`);
  console.log(`GOOGLE_PLAY_OAUTH_CLIENT_SECRET=${clientSecret}`);
  console.log(`GOOGLE_PLAY_OAUTH_REFRESH_TOKEN=${tokens.refresh_token}`);
}

main().catch((error) => {
  console.error(error.message || error);
  process.exitCode = 1;
});
