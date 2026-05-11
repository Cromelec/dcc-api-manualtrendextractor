// ══════════════════════════════════════════════════════════════
//  tokenMonitor.js — Surveillance locale du token NORIS API
//  Basé sur la doc NORIS API v8.0 (section 4.1.1 & 7.2.1)
//  Inactivité max : 10 minutes (valeur par défaut serveur)
// ══════════════════════════════════════════════════════════════

const TokenMonitor = (() => {

    // ── Constantes ────────────────────────────────────────────
    const INACTIVITY_LIMIT_MS = 10 * 60 * 1000; // 10 min (doc NORIS)
    const WARN_THRESHOLD_MS   =  2 * 60 * 1000; // Avertir à 2 min restantes
    const CHECK_INTERVAL_MS   =     30 * 1000;  // Vérifier toutes les 30s

    // ── État interne ──────────────────────────────────────────
    let _loginTime        = null;
    let _expiresInMs      = null;
    let _lastActivityTime = null;
    let _intervalId       = null;
    let _isExpired        = false;

    // ══════════════════════════════════════════════════════════
    //  API PUBLIQUE
    // ══════════════════════════════════════════════════════════

    // ── Appelé après un login / refresh réussi ────────────────
    function onLogin(expiresInSeconds) {
        _loginTime        = Date.now();
        _lastActivityTime = Date.now();
        _expiresInMs      = expiresInSeconds * 1000;
        _isExpired        = false;
        _startMonitoring();
        console.log(`[TokenMonitor] ✅ Démarré — expire dans ${expiresInSeconds}s`);
    }

    // ── Appelé après chaque appel API réussi ─────────────────
    function registerActivity() {
        if (_isExpired) return;
        _lastActivityTime = Date.now();
    }

    // ── Appelé lors du logout ou d'un 401 ────────────────────
    function onLogout() {
        _stopMonitoring();
        _loginTime        = null;
        _lastActivityTime = null;
        _expiresInMs      = null;
        _isExpired        = false;
        console.log("[TokenMonitor] 🔴 Arrêté (logout / 401)");
    }

    // ══════════════════════════════════════════════════════════
    //  LOGIQUE INTERNE
    // ══════════════════════════════════════════════════════════

    // ── Vérification locale — AUCUN appel API ─────────────────
    function _check() {
        if (!_loginTime || _isExpired) return;

        const now             = Date.now();
        const inactiveMs      = now - _lastActivityTime;
        const absoluteElapsed = now - _loginTime;

        // ── Cas 1 : Expiration absolue (expires_in dépassé) ──
        if (absoluteElapsed >= _expiresInMs) {
            _triggerExpired("Token expiré (limite absolue atteinte)");
            return;
        }

        // ── Cas 2 : Expiration par inactivité (10 min) ────────
        if (inactiveMs >= INACTIVITY_LIMIT_MS) {
            _triggerExpired("Token expiré (inactivité > 10 min)");
            return;
        }

        // ── Cas 3 : Avertissement (< 2 min restantes) ─────────
        const remainingMs = INACTIVITY_LIMIT_MS - inactiveMs;
        if (remainingMs <= WARN_THRESHOLD_MS) {
            const remainingSec = Math.floor(remainingMs / 1000);
            _triggerWarning(`⚠️ Token expire dans ${remainingSec}s sans activité`);
        } else {
            _triggerOk();
        }
    }

    // ── Déclencheurs d'événements DOM ─────────────────────────
    function _triggerExpired(reason) {
        _isExpired = true;
        _stopMonitoring();
        console.warn("[TokenMonitor] ⛔", reason);
        document.dispatchEvent(
            new CustomEvent("tokenExpired", { detail: { reason } })
        );
    }

    function _triggerWarning(message) {
        document.dispatchEvent(
            new CustomEvent("tokenWarning", { detail: { message } })
        );
    }

    function _triggerOk() {
        document.dispatchEvent(new CustomEvent("tokenOk"));
    }

    // ── Gestion du timer ──────────────────────────────────────
    function _startMonitoring() {
        _stopMonitoring();
        _intervalId = setInterval(_check, CHECK_INTERVAL_MS);
    }

    function _stopMonitoring() {
        if (_intervalId) {
            clearInterval(_intervalId);
            _intervalId = null;
        }
    }

    // ── Exposition publique ───────────────────────────────────
    return { onLogin, onLogout, registerActivity };

})();