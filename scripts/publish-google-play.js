#!/usr/bin/env node
'use strict';

const dotenv = require('dotenv');
dotenv.config({ path: '.env.local' });
dotenv.config();

const fs = require('fs');
const path = require('path');
const { GoogleAuth, JWT, OAuth2Client } = require('google-auth-library');

const ANDROID_PUBLISHER_SCOPE = 'https://www.googleapis.com/auth/androidpublisher';
const API_BASE = 'https://androidpublisher.googleapis.com/androidpublisher/v3/applications';
const UPLOAD_BASE = 'https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications';

function printUsage() {
  console.log(`
Usage:
  node scripts/publish-google-play.js [options]

Required options or env vars:
  --package-name                or GOOGLE_PLAY_PACKAGE_NAME
  --aab-path                    or GOOGLE_PLAY_AAB_PATH

Authentication options:
  --auth-mode                   or GOOGLE_PLAY_AUTH_MODE
                                one of: adc, oauth, service-account
  ADC / Workload Identity:
    GOOGLE_APPLICATION_CREDENTIALS
    or gcloud application-default login
  OAuth refresh token:
    --oauth-client-id           or GOOGLE_PLAY_OAUTH_CLIENT_ID
    --oauth-client-secret       or GOOGLE_PLAY_OAUTH_CLIENT_SECRET
    --oauth-refresh-token       or GOOGLE_PLAY_OAUTH_REFRESH_TOKEN
  Legacy service account key:
    --service-account-file      or GOOGLE_PLAY_SERVICE_ACCOUNT_FILE
    --service-account-json      or GOOGLE_PLAY_SERVICE_ACCOUNT_JSON
    --service-account-json-base64 or GOOGLE_PLAY_SERVICE_ACCOUNT_JSON_BASE64

Optional options or env vars:
  --track                       or GOOGLE_PLAY_TRACK                  (default: internal)
  --release-name                or GOOGLE_PLAY_RELEASE_NAME          (default: AAB filename)
  --release-status              or GOOGLE_PLAY_RELEASE_STATUS        (default: completed)
  --release-notes-file          or GOOGLE_PLAY_RELEASE_NOTES_FILE
  --release-notes-language      or GOOGLE_PLAY_RELEASE_NOTES_LANGUAGE (default: en-US)
  --release-notes-text          or GOOGLE_PLAY_RELEASE_NOTES_TEXT
  --user-fraction               or GOOGLE_PLAY_USER_FRACTION
  --in-app-update-priority      or GOOGLE_PLAY_IN_APP_UPDATE_PRIORITY
  --changes-not-sent-for-review or GOOGLE_PLAY_CHANGES_NOT_SENT_FOR_REVIEW
  --dry-run
  --help

Release notes file formats:
  1. Plain text: used as a single release note with --release-notes-language
  2. JSON array: [{ "language": "en-US", "text": "..." }]
  3. JSON object: { "en-US": "...", "es-ES": "..." }
`.trim());
}

function parseArgs(argv) {
  const args = {};

  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (!token.startsWith('--')) {
      throw new Error(`Unexpected argument: ${token}`);
    }

    const withoutPrefix = token.slice(2);
    const equalIndex = withoutPrefix.indexOf('=');

    if (equalIndex >= 0) {
      const key = withoutPrefix.slice(0, equalIndex);
      const value = withoutPrefix.slice(equalIndex + 1);
      args[key] = value;
      continue;
    }

    const next = argv[i + 1];
    if (!next || next.startsWith('--')) {
      args[withoutPrefix] = 'true';
      continue;
    }

    args[withoutPrefix] = next;
    i += 1;
  }

  return args;
}

function readBool(value, fallback = false) {
  if (value === undefined || value === null || value === '') {
    return fallback;
  }

  const normalized = String(value).trim().toLowerCase();
  if (['1', 'true', 'yes', 'y', 'on'].includes(normalized)) {
    return true;
  }
  if (['0', 'false', 'no', 'n', 'off'].includes(normalized)) {
    return false;
  }

  throw new Error(`Invalid boolean value: ${value}`);
}

function readNumber(value, fieldName) {
  if (value === undefined || value === null || value === '') {
    return undefined;
  }

  const parsed = Number(value);
  if (Number.isNaN(parsed)) {
    throw new Error(`${fieldName} must be a number.`);
  }

  return parsed;
}

function getOption(args, key, envKey) {
  return args[key] ?? process.env[envKey];
}

function resolvePath(inputPath) {
  return path.isAbsolute(inputPath) ? inputPath : path.resolve(process.cwd(), inputPath);
}

function loadServiceAccount(args) {
  const jsonBase64 = getOption(args, 'service-account-json-base64', 'GOOGLE_PLAY_SERVICE_ACCOUNT_JSON_BASE64');
  if (jsonBase64) {
    const rawJson = Buffer.from(jsonBase64, 'base64').toString('utf8');
    return normalizeServiceAccount(JSON.parse(rawJson));
  }

  const jsonValue = getOption(args, 'service-account-json', 'GOOGLE_PLAY_SERVICE_ACCOUNT_JSON');
  if (jsonValue) {
    if (jsonValue.trim().startsWith('{')) {
      return normalizeServiceAccount(JSON.parse(jsonValue));
    }

    const jsonPath = resolvePath(jsonValue);
    return normalizeServiceAccount(JSON.parse(fs.readFileSync(jsonPath, 'utf8')));
  }

  const fileValue =
    getOption(args, 'service-account-file', 'GOOGLE_PLAY_SERVICE_ACCOUNT_FILE')
    || process.env.GOOGLE_APPLICATION_CREDENTIALS;

  if (!fileValue) {
    throw new Error(
      'Missing Google Play service account credentials. Set GOOGLE_PLAY_SERVICE_ACCOUNT_FILE, GOOGLE_PLAY_SERVICE_ACCOUNT_JSON, or GOOGLE_PLAY_SERVICE_ACCOUNT_JSON_BASE64.'
    );
  }

  const filePath = resolvePath(fileValue);
  return normalizeServiceAccount(JSON.parse(fs.readFileSync(filePath, 'utf8')));
}

function normalizeServiceAccount(serviceAccount) {
  if (!serviceAccount || typeof serviceAccount !== 'object') {
    throw new Error('Service account credentials must be a JSON object.');
  }

  if (!serviceAccount.client_email || !serviceAccount.private_key) {
    throw new Error('Service account JSON must include client_email and private_key.');
  }

  return {
    clientEmail: serviceAccount.client_email,
    privateKey: String(serviceAccount.private_key).replace(/\\n/g, '\n'),
  };
}

function inferAuthMode(args) {
  const explicit = getOption(args, 'auth-mode', 'GOOGLE_PLAY_AUTH_MODE');
  if (explicit) {
    return explicit;
  }

  const hasOAuth =
    !!getOption(args, 'oauth-client-id', 'GOOGLE_PLAY_OAUTH_CLIENT_ID')
    || !!getOption(args, 'oauth-client-secret', 'GOOGLE_PLAY_OAUTH_CLIENT_SECRET')
    || !!getOption(args, 'oauth-refresh-token', 'GOOGLE_PLAY_OAUTH_REFRESH_TOKEN');

  if (hasOAuth) {
    return 'oauth';
  }

  const hasLegacyServiceAccount =
    !!getOption(args, 'service-account-file', 'GOOGLE_PLAY_SERVICE_ACCOUNT_FILE')
    || !!getOption(args, 'service-account-json', 'GOOGLE_PLAY_SERVICE_ACCOUNT_JSON')
    || !!getOption(args, 'service-account-json-base64', 'GOOGLE_PLAY_SERVICE_ACCOUNT_JSON_BASE64');

  if (hasLegacyServiceAccount) {
    return 'service-account';
  }

  return 'adc';
}

function loadAuthConfig(args) {
  const authMode = inferAuthMode(args);
  if (!['adc', 'oauth', 'service-account'].includes(authMode)) {
    throw new Error('auth-mode must be one of: adc, oauth, service-account.');
  }

  if (authMode === 'oauth') {
    const clientId = getOption(args, 'oauth-client-id', 'GOOGLE_PLAY_OAUTH_CLIENT_ID');
    const clientSecret = getOption(args, 'oauth-client-secret', 'GOOGLE_PLAY_OAUTH_CLIENT_SECRET');
    const refreshToken = getOption(args, 'oauth-refresh-token', 'GOOGLE_PLAY_OAUTH_REFRESH_TOKEN');

    if (!clientId || !clientSecret || !refreshToken) {
      throw new Error(
        'OAuth mode requires GOOGLE_PLAY_OAUTH_CLIENT_ID, GOOGLE_PLAY_OAUTH_CLIENT_SECRET, and GOOGLE_PLAY_OAUTH_REFRESH_TOKEN.'
      );
    }

    return {
      mode: 'oauth',
      clientId,
      clientSecret,
      refreshToken,
    };
  }

  if (authMode === 'service-account') {
    return {
      mode: 'service-account',
      serviceAccount: loadServiceAccount(args),
    };
  }

  return {
    mode: 'adc',
  };
}

function loadReleaseNotes(config) {
  if (config.releaseNotesFile) {
    const releaseNotesPath = resolvePath(config.releaseNotesFile);
    const raw = fs.readFileSync(releaseNotesPath, 'utf8').trim();
    if (!raw) {
      return [];
    }

    if (releaseNotesPath.endsWith('.json')) {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        return parsed.map(validateReleaseNote);
      }

      if (parsed && typeof parsed === 'object') {
        return Object.entries(parsed).map(([language, text]) =>
          validateReleaseNote({ language, text })
        );
      }

      throw new Error('Release notes JSON must be an array or object.');
    }

    return [validateReleaseNote({
      language: config.releaseNotesLanguage,
      text: raw,
    })];
  }

  if (config.releaseNotesText) {
    return [validateReleaseNote({
      language: config.releaseNotesLanguage,
      text: config.releaseNotesText,
    })];
  }

  return [];
}

function validateReleaseNote(note) {
  if (!note || typeof note !== 'object') {
    throw new Error('Each release note must be an object.');
  }

  const language = String(note.language || '').trim();
  const text = String(note.text || '').trim();

  if (!language || !text) {
    throw new Error('Each release note must include language and text.');
  }

  return { language, text };
}

function buildConfig(args) {
  const packageName = getOption(args, 'package-name', 'GOOGLE_PLAY_PACKAGE_NAME');
  const aabPathInput = getOption(args, 'aab-path', 'GOOGLE_PLAY_AAB_PATH');

  if (!packageName) {
    throw new Error('Missing package name. Set --package-name or GOOGLE_PLAY_PACKAGE_NAME.');
  }

  if (!aabPathInput) {
    throw new Error('Missing AAB path. Set --aab-path or GOOGLE_PLAY_AAB_PATH.');
  }

  const aabPath = resolvePath(aabPathInput);
  if (!fs.existsSync(aabPath)) {
    throw new Error(`AAB file not found: ${aabPath}`);
  }

  const releaseStatus = getOption(args, 'release-status', 'GOOGLE_PLAY_RELEASE_STATUS') || 'completed';
  const track = getOption(args, 'track', 'GOOGLE_PLAY_TRACK') || 'internal';
  const releaseName =
    getOption(args, 'release-name', 'GOOGLE_PLAY_RELEASE_NAME')
    || path.basename(aabPath, path.extname(aabPath));
  const releaseNotesLanguage =
    getOption(args, 'release-notes-language', 'GOOGLE_PLAY_RELEASE_NOTES_LANGUAGE')
    || 'en-US';
  const userFraction = readNumber(
    getOption(args, 'user-fraction', 'GOOGLE_PLAY_USER_FRACTION'),
    'user-fraction'
  );
  const inAppUpdatePriority = readNumber(
    getOption(args, 'in-app-update-priority', 'GOOGLE_PLAY_IN_APP_UPDATE_PRIORITY'),
    'in-app-update-priority'
  );
  const dryRun = readBool(args['dry-run'], false);
  const changesNotSentForReview = readBool(
    getOption(args, 'changes-not-sent-for-review', 'GOOGLE_PLAY_CHANGES_NOT_SENT_FOR_REVIEW'),
    false
  );

  if (!['draft', 'inProgress', 'halted', 'completed'].includes(releaseStatus)) {
    throw new Error('release-status must be one of: draft, inProgress, halted, completed.');
  }

  if (releaseStatus === 'inProgress') {
    if (userFraction === undefined || userFraction <= 0 || userFraction >= 1) {
      throw new Error('inProgress releases require user-fraction between 0 and 1.');
    }
  } else if (userFraction !== undefined) {
    throw new Error('user-fraction is only valid when release-status is inProgress.');
  }

  if (inAppUpdatePriority !== undefined) {
    if (!Number.isInteger(inAppUpdatePriority) || inAppUpdatePriority < 0 || inAppUpdatePriority > 5) {
      throw new Error('in-app-update-priority must be an integer from 0 to 5.');
    }
  }

  const config = {
    packageName,
    aabPath,
    track,
    releaseName,
    releaseStatus,
    releaseNotesFile: getOption(args, 'release-notes-file', 'GOOGLE_PLAY_RELEASE_NOTES_FILE'),
    releaseNotesLanguage,
    releaseNotesText: getOption(args, 'release-notes-text', 'GOOGLE_PLAY_RELEASE_NOTES_TEXT'),
    userFraction,
    inAppUpdatePriority,
    dryRun,
    changesNotSentForReview,
  };

  config.releaseNotes = loadReleaseNotes(config);
  config.auth = loadAuthConfig(args);

  return config;
}

async function createAuthClient(authConfig) {
  if (authConfig.mode === 'service-account') {
    return new JWT({
      email: authConfig.serviceAccount.clientEmail,
      key: authConfig.serviceAccount.privateKey,
      scopes: [ANDROID_PUBLISHER_SCOPE],
    });
  }

  if (authConfig.mode === 'oauth') {
    const client = new OAuth2Client(authConfig.clientId, authConfig.clientSecret);
    client.setCredentials({ refresh_token: authConfig.refreshToken });
    return client;
  }

  const auth = new GoogleAuth({
    scopes: [ANDROID_PUBLISHER_SCOPE],
  });

  return auth.getClient();
}

async function getAccessToken(authClient) {
  if (typeof authClient.authorize === 'function') {
    const tokens = await authClient.authorize();
    if (tokens.access_token) {
      return tokens.access_token;
    }
  }

  const accessTokenResult = await authClient.getAccessToken();
  const accessToken = typeof accessTokenResult === 'string'
    ? accessTokenResult
    : accessTokenResult?.token;

  if (!accessToken) {
    throw new Error('Failed to obtain a Google access token.');
  }

  return accessToken;
}

async function parseResponse(response) {
  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return response.json();
  }

  const text = await response.text();
  return text ? { raw: text } : {};
}

async function googleRequest(url, token, options = {}) {
  const { method = 'GET', body, headers = {} } = options;

  const requestHeaders = {
    Authorization: `Bearer ${token}`,
    ...headers,
  };

  let requestBody;
  if (body !== undefined) {
    requestHeaders['Content-Type'] = requestHeaders['Content-Type'] || 'application/json; charset=utf-8';
    requestBody = JSON.stringify(body);
  }

  const response = await fetch(url, {
    method,
    headers: requestHeaders,
    body: requestBody,
  });

  if (!response.ok) {
    const errorBody = await parseResponse(response);
    throw new Error(
      `Google Play API request failed (${response.status} ${response.statusText}) at ${url}\n${JSON.stringify(errorBody, null, 2)}`
    );
  }

  return parseResponse(response);
}

async function createEdit(config, token) {
  const url = `${API_BASE}/${encodeURIComponent(config.packageName)}/edits`;
  return googleRequest(url, token, { method: 'POST', body: {} });
}

async function uploadBundle(config, token, editId) {
  const stats = fs.statSync(config.aabPath);
  const startUrl = `${UPLOAD_BASE}/${encodeURIComponent(config.packageName)}/edits/${encodeURIComponent(editId)}/bundles?uploadType=resumable`;

  const startResponse = await fetch(startUrl, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json; charset=utf-8',
      'X-Upload-Content-Type': 'application/octet-stream',
      'X-Upload-Content-Length': String(stats.size),
    },
    body: '{}',
  });

  if (!startResponse.ok) {
    const errorBody = await parseResponse(startResponse);
    throw new Error(
      `Failed to start Play bundle upload (${startResponse.status} ${startResponse.statusText})\n${JSON.stringify(errorBody, null, 2)}`
    );
  }

  const uploadUrl = startResponse.headers.get('location');
  if (!uploadUrl) {
    throw new Error('Google Play did not return a resumable upload URL.');
  }

  const fileStream = fs.createReadStream(config.aabPath);
  const uploadResponse = await fetch(uploadUrl, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/octet-stream',
      'Content-Length': String(stats.size),
    },
    body: fileStream,
    duplex: 'half',
  });

  if (!uploadResponse.ok) {
    const errorBody = await parseResponse(uploadResponse);
    throw new Error(
      `Bundle upload failed (${uploadResponse.status} ${uploadResponse.statusText})\n${JSON.stringify(errorBody, null, 2)}`
    );
  }

  return parseResponse(uploadResponse);
}

function buildTrackPayload(config, versionCode) {
  const release = {
    name: config.releaseName,
    status: config.releaseStatus,
    versionCodes: [String(versionCode)],
  };

  if (config.releaseNotes.length > 0) {
    release.releaseNotes = config.releaseNotes;
  }

  if (config.userFraction !== undefined) {
    release.userFraction = config.userFraction;
  }

  if (config.inAppUpdatePriority !== undefined) {
    release.inAppUpdatePriority = config.inAppUpdatePriority;
  }

  return { releases: [release] };
}

async function updateTrack(config, token, editId, versionCode) {
  const url =
    `${API_BASE}/${encodeURIComponent(config.packageName)}/edits/${encodeURIComponent(editId)}/tracks/${encodeURIComponent(config.track)}`;

  return googleRequest(url, token, {
    method: 'PUT',
    body: buildTrackPayload(config, versionCode),
  });
}

async function commitEdit(config, token, editId) {
  const query = config.changesNotSentForReview ? '?changesNotSentForReview=true' : '';
  const url =
    `${API_BASE}/${encodeURIComponent(config.packageName)}/edits/${encodeURIComponent(editId)}:commit${query}`;

  return googleRequest(url, token, { method: 'POST' });
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    printUsage();
    return;
  }

  const config = buildConfig(args);
  console.log(`Prepared Google Play release for ${config.packageName}`);
  console.log(`Track: ${config.track}`);
  console.log(`AAB: ${config.aabPath}`);
  console.log(`Status: ${config.releaseStatus}`);

  if (config.dryRun) {
    console.log('Dry run complete. Config parsed and required files were found.');
    return;
  }

  const authClient = await createAuthClient(config.auth);
  const token = await getAccessToken(authClient);
  console.log(`Authenticated with Google Play Developer API using ${config.auth.mode}.`);

  const edit = await createEdit(config, token);
  console.log(`Created edit ${edit.id}.`);

  const bundle = await uploadBundle(config, token, edit.id);
  const versionCode = bundle.versionCode;
  console.log(`Uploaded bundle with version code ${versionCode}.`);

  await updateTrack(config, token, edit.id, versionCode);
  console.log(`Assigned version code ${versionCode} to the ${config.track} track.`);

  await commitEdit(config, token, edit.id);
  console.log(`Committed edit ${edit.id}. Release is now submitted to Google Play.`);
}

main().catch((error) => {
  console.error(error.message || error);
  process.exitCode = 1;
});
