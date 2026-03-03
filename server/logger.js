'use strict';

// Structured JSON logger — outputs one JSON line per call to stdout (or stderr for errors).
// Railway streams stdout to its log drain, so JSON lines are natively parsed there.
//
// Usage:
//   const log = require('./logger');
//   log.info('server started', { port: 3000 });
//   log.warn('slow query', { ms: 450 });
//   log.error('db failed', { err: e.message });

function _write(level, msg, data) {
  const entry = Object.assign({ level, ts: new Date().toISOString(), msg }, data);
  const line = JSON.stringify(entry) + '\n';
  if (level === 'error') process.stderr.write(line);
  else process.stdout.write(line);
}

module.exports = {
  info:  (msg, data) => _write('info',  msg, data),
  warn:  (msg, data) => _write('warn',  msg, data),
  error: (msg, data) => _write('error', msg, data),
};
