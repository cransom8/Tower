// SocketIOBridge.jslib
// Bridges the browser's native Socket.IO 4.x client to Unity C#.
//
// Each function that reads/writes $JSIO must declare __deps: ['$JSIO'].
// Without that, Emscripten strips $JSIO from the function's closure and
// the runtime throws "JSIO is not defined".

mergeInto(LibraryManager.library, {

    // ── Shared state ──────────────────────────────────────────────────────────
    $JSIO: {
        socket          : null,
        goName          : '',
        pendingListeners: []
    },
    $JSIO__deps: [],

    // ── JWT helpers (localStorage) ────────────────────────────────────────────
    JSIO_GetJWT: function() {
        var token = localStorage.getItem('castle_jwt') || '';
        var buf   = lengthBytesUTF8(token) + 1;
        var ptr   = _malloc(buf);
        stringToUTF8(token, ptr, buf);
        return ptr;
    },

    JSIO_SetJWT: function(tokenPtr) {
        var token = UTF8ToString(tokenPtr);
        if (token) localStorage.setItem('castle_jwt', token);
    },

    JSIO_ClearJWT: function() {
        localStorage.removeItem('castle_jwt');
    },

    // ── Connect ───────────────────────────────────────────────────────────────
    JSIO_Connect__deps: ['$JSIO'],
    JSIO_Connect: function(urlPtr, goNamePtr) {
        var url    = UTF8ToString(urlPtr);
        var goName = UTF8ToString(goNamePtr);
        JSIO.goName = goName;

        function _setupSocket() {
            // Pass JWT as Socket.IO auth if present in localStorage
            var token   = localStorage.getItem('castle_jwt') || null;
            var authOpt = token ? { token: token } : undefined;

            JSIO.socket = io(url, {
                reconnectionAttempts: 5,
                reconnectionDelay   : 2000,
                transports          : ['websocket', 'polling'],
                auth                : authOpt
            });

            JSIO.socket.on('connect', function() {
                SendMessage(JSIO.goName, 'OnJSIO_connect', JSIO.socket.id || '');
            });

            JSIO.socket.on('disconnect', function(reason) {
                SendMessage(JSIO.goName, 'OnJSIO_disconnect', reason || '');
            });

            JSIO.socket.on('connect_error', function(err) {
                var msg = err ? (err.message || err.toString()) : 'unknown error';
                SendMessage(JSIO.goName, 'OnJSIO_error', msg);
            });

            // Flush any listeners registered before the socket was ready
            for (var i = 0; i < JSIO.pendingListeners.length; i++) {
                (function(ev) {
                    JSIO.socket.on(ev, function(data) {
                        var json = (data !== undefined && data !== null)
                            ? JSON.stringify(data) : '{}';
                        SendMessage(JSIO.goName, 'OnJSIOEvent', ev + '\x01' + json);
                    });
                })(JSIO.pendingListeners[i]);
            }
            JSIO.pendingListeners = [];
        }

        if (typeof io !== 'undefined') {
            _setupSocket();
        } else {
            var script     = document.createElement('script');
            script.src     = url + '/socket.io/socket.io.js';
            script.onload  = function() { _setupSocket(); };
            script.onerror = function() {
                SendMessage(JSIO.goName, 'OnJSIO_error',
                    'Failed to load ' + url + '/socket.io/socket.io.js');
            };
            document.head.appendChild(script);
        }
    },

    // ── Register event listener ───────────────────────────────────────────────
    JSIO_On__deps: ['$JSIO'],
    JSIO_On: function(eventPtr) {
        var ev = UTF8ToString(eventPtr);
        if (JSIO.socket) {
            var goName = JSIO.goName;
            JSIO.socket.on(ev, function(data) {
                var json = (data !== undefined && data !== null)
                    ? JSON.stringify(data) : '{}';
                SendMessage(goName, 'OnJSIOEvent', ev + '\x01' + json);
            });
        } else {
            JSIO.pendingListeners.push(ev);
        }
    },

    // ── Emit ──────────────────────────────────────────────────────────────────
    JSIO_Emit__deps: ['$JSIO'],
    JSIO_Emit: function(eventPtr, dataPtr) {
        if (!JSIO.socket || !JSIO.socket.connected) return;
        var ev   = UTF8ToString(eventPtr);
        var data = UTF8ToString(dataPtr);
        if (data && data.length > 0) {
            try   { JSIO.socket.emit(ev, JSON.parse(data)); }
            catch (e) { JSIO.socket.emit(ev, data); }
        } else {
            JSIO.socket.emit(ev);
        }
    },

    // ── Disconnect ────────────────────────────────────────────────────────────
    JSIO_Disconnect__deps: ['$JSIO'],
    JSIO_Disconnect: function() {
        if (JSIO.socket) {
            JSIO.socket.disconnect();
            JSIO.socket = null;
        }
    },

    // ── Google SSO — trigger One Tap, send credential back to C# via SendMessage ──
    // C# must have a public method:  void OnGoogleCredential(string credential)
    JSIO_GoogleSignIn: function(clientIdPtr, gameObjectNamePtr) {
        var clientId = UTF8ToString(clientIdPtr);
        var goName   = UTF8ToString(gameObjectNamePtr);
        var state    = window.__castleGoogleAuthState || (window.__castleGoogleAuthState = {
            initialized: false,
            loadingScript: false,
            inFlight: false,
            clientId: '',
            goName: ''
        });

        state.clientId = clientId;
        state.goName   = goName;

        function finishWithCredential(cred) {
            state.inFlight = false;
            if (cred) {
                SendMessage(state.goName, 'OnGoogleCredential', cred);
            }
        }

        function initAndPrompt() {
            if (!google || !google.accounts || !google.accounts.id) {
                state.inFlight = false;
                SendMessage(state.goName, 'OnGoogleCredential', '');
                return;
            }

            if (!state.initialized || state.clientId !== clientId) {
                google.accounts.id.initialize({
                    client_id            : clientId,
                    callback             : function(response) {
                        var cred = (response && response.credential) ? response.credential : '';
                        finishWithCredential(cred);
                    },
                    cancel_on_tap_outside: false,
                    auto_select          : false
                });
                state.initialized = true;
            }

            if (state.inFlight) return;
            state.inFlight = true;

            google.accounts.id.prompt(function(notification) {
                // Prompt status callbacks are informational only here.
                // Do not emit an empty credential because that races the real callback.
                if (notification && notification.isDismissedMoment && notification.isDismissedMoment()) {
                    state.inFlight = false;
                }
            });
        }

        if (typeof google !== 'undefined' && google.accounts && google.accounts.id) {
            initAndPrompt();
        } else if (!state.loadingScript) {
            state.loadingScript = true;
            var script = document.createElement('script');
            script.src = 'https://accounts.google.com/gsi/client';
            script.async = true;
            script.onload = function() {
                state.loadingScript = false;
                initAndPrompt();
            };
            script.onerror = function() {
                state.loadingScript = false;
                state.inFlight = false;
                SendMessage(state.goName, 'OnGoogleCredential', '');
            };
            document.head.appendChild(script);
        }
    }
});
