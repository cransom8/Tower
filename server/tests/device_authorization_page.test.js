'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');

const { renderDeviceAuthorizationPage } = require('../deviceAuthPages');

test('renderDeviceAuthorizationPage checks the live auth session endpoint', () => {
  const html = renderDeviceAuthorizationPage({
    appName: 'RansomForge',
    googleClientId: 'test-google-client-id',
    code: 'ABC123',
  });

  assert.match(html, /fetch\("\/auth\/session"/);
  assert.doesNotMatch(html, /fetch\("\/auth\/me"/);
});

test('renderDeviceAuthorizationPage shows clearer errors for non-JSON auth responses', () => {
  const html = renderDeviceAuthorizationPage();

  assert.match(html, /unexpected session response/);
  assert.match(html, /unexpected response while authorizing the device/);
  assert.match(html, /unexpected response while finishing Google sign-in/);
});
