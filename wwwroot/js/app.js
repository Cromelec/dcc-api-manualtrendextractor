// ══════════════════════════════════════════
//  Variables globales
// ══════════════════════════════════════════
const BASE_URL = "/Trend";

let connection   = null;
let connectionId = null;
let currentMode  = "single";
let allTrends    = []; // 🆕 Cache de la liste complète des trends

// ══════════════════════════════════════════
//  SignalR — Init
// ══════════════════════════════════════════
function initSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/Trend/trendHub")
        .withAutomaticReconnect()
        .build();

    // ── Handlers SignalR ──
    connection.on("Progress", (data) => {
        showCard("cardProgress");
        document.getElementById("progressFill").style.width     = data.percent + "%";
        document.getElementById("progressLabel").textContent    = `${data.current} / ${data.total}`;
        document.getElementById("progressLocation").textContent = data.location;
        setDot("dotAcq", "active");
        appendLog(data.log);
    });

    connection.on("Log", (message) => {
        appendLog(message);
    });

    connection.on("TokenExpired", () => {
        TokenMonitor.onLogout();
        setTokenExpired();
    });

    startSignalR();
}

// ── Démarrage de la connexion ──
async function startSignalR() {
    try {
        await connection.start();
        connectionId = await connection.invoke("GetConnectionId");
        appendLog("🔗 Connexion SignalR établie.");
        checkStatus();
    } catch (err) {
        appendLog("❌ Erreur SignalR : " + err);
        setTimeout(startSignalR, 3000);
    }
}

// ══════════════════════════════════════════
//  TokenMonitor — Écouteurs d'événements
// ══════════════════════════════════════════
function initTokenMonitorListeners() {

    document.addEventListener("tokenExpired", (e) => {
        setTokenExpired();
        appendLog(`⛔ [TokenMonitor] ${e.detail.reason}`);
    });

    document.addEventListener("tokenWarning", (e) => {
        setDot("dotToken", "warning");
        document.getElementById("lblTokenStatus").textContent =
            e.detail.message;
        appendLog(`⚠️ [TokenMonitor] ${e.detail.message}`);
    });

    document.addEventListener("tokenOk", () => {
        setDot("dotToken",   "ok");
        setDot("dotSession", "ok");
        document.getElementById("lblTokenStatus").textContent =
            "Token : actif";
    });
}

// ══════════════════════════════════════════
//  Mode toggle
// ══════════════════════════════════════════
function setMode(mode) {
    currentMode = mode;

    document.getElementById("groupSingle")
        .classList.toggle("si-hidden", mode !== "single");
    document.getElementById("groupRange")
        .classList.toggle("si-hidden", mode !== "range");

    document.getElementById("btnModeSingle")
        .classList.toggle("active", mode === "single");
    document.getElementById("btnModeRange")
        .classList.toggle("active", mode === "range");
}

// ══════════════════════════════════════════
//  🆕 Trend List — Chargement & Sélection
// ══════════════════════════════════════════
async function loadTrendList() {
    const btn = document.getElementById("btnLoadTrends");
    btn.disabled    = true;
    btn.textContent = "⏳ Chargement...";

    appendLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    appendLog("📡 Chargement de la liste des trends...");

    try {
        const res = await fetch(`${BASE_URL}/api/trend/list`);

        if (res.status === 401) {
            TokenMonitor.onLogout();
            setTokenExpired();
            appendLog("❌ Token expiré — impossible de charger la liste.");
            return;
        }

        const trends = await res.json();
        allTrends    = trends;

        renderTrendList(trends);
        updateTrendCount();

        appendLog(`✅ ${trends.length} trend(s) chargé(s).`);

    } catch (err) {
        appendLog(`❌ Erreur chargement liste : ${err.message}`);
    } finally {
        btn.disabled    = false;
        btn.textContent = "🔄 Charger la liste";
    }
}

function renderTrendList(trends) {
    const container = document.getElementById("trendListContainer");

    if (trends.length === 0) {
        container.innerHTML = `
            <div style="padding:16px; text-align:center; color:#999;">
                Aucun trend disponible.
            </div>`;
        return;
    }

    container.innerHTML = trends.map(t => `
        <label class="si-trend-item" data-id="${escHtml(t.trendseriesId)}">
            <input type="checkbox"
                   class="trend-checkbox"
                   value="${escHtml(t.trendseriesId)}"
                   checked />
            <span class="si-trend-label">${escHtml(t.formattedLocation)}</span>
        </label>
    `).join("");

    container.querySelectorAll(".trend-checkbox").forEach(cb => {
        cb.addEventListener("change", updateTrendCount);
    });

    updateTrendCount();
}

function filterTrends(query) {
    const q     = query.toLowerCase().trim();
    const items = document.querySelectorAll(".si-trend-item");

    items.forEach(item => {
        const label = item.querySelector(".si-trend-label")?.textContent.toLowerCase() ?? "";
        item.style.display = (!q || label.includes(q)) ? "" : "none";
    });
}

function selectAllTrends(checked) {
    document.querySelectorAll(".trend-checkbox").forEach(cb => {
        const item = cb.closest(".si-trend-item");
        if (item && item.style.display !== "none")
            cb.checked = checked;
    });
    updateTrendCount();
}

function updateTrendCount() {
    const total    = document.querySelectorAll(".trend-checkbox").length;
    const selected = document.querySelectorAll(".trend-checkbox:checked").length;
    const lbl      = document.getElementById("lblTrendCount");

    if (total === 0) {
        lbl.textContent = "Aucun trend chargé.";
        lbl.style.color = "#666";
    } else {
        lbl.textContent = `${selected} / ${total} trend(s) sélectionné(s)`;
        lbl.style.color = selected === 0 ? "#e53935" : "#666";
    }
}

function getSelectedTrendIds() {
    return Array.from(document.querySelectorAll(".trend-checkbox:checked"))
                .map(cb => cb.value);
}

function escHtml(str) {
    return (str ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}

// ══════════════════════════════════════════
//  Acquisition — Démarrage
// ══════════════════════════════════════════
async function startAcquisition() {
    if (!connectionId) {
        appendLog("⚠️ Connexion SignalR non prête, veuillez patienter...");
        return;
    }

    // ── Validation dates ──
    let payload = { mode: currentMode };

    if (currentMode === "single") {
        const d = document.getElementById("dateSingle").value;
        if (!d) { appendLog("⚠️ Veuillez sélectionner une date."); return; }
        payload.dateSingle = d;
    } else {
        const from = document.getElementById("dateFrom").value;
        const to   = document.getElementById("dateTo").value;
        if (!from || !to) { appendLog("⚠️ Veuillez renseigner les deux dates."); return; }
        if (from > to)    { appendLog("⚠️ La date de début doit être avant la date de fin."); return; }
        payload.dateFrom = from;
        payload.dateTo   = to;
    }

    // ── 🆕 Validation & ajout des trends sélectionnés ──
    const selectedIds = getSelectedTrendIds();

    if (allTrends.length > 0 && selectedIds.length === 0) {
        appendLog("⚠️ Aucun trend sélectionné. Veuillez en sélectionner au moins un.");
        return;
    }

    // Si la liste n'a pas été chargée → null = tous les trends côté serveur
    payload.selectedIds = allTrends.length > 0 ? selectedIds : null;

    // ── UI : état "en cours" ──
    setRunning(true);
    hideCard("cardResult");
    showCard("cardProgress");
    document.getElementById("progressFill").style.width     = "0%";
    document.getElementById("progressLabel").textContent    = "0 / 0";
    document.getElementById("progressLocation").textContent = "Initialisation...";

    appendLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    appendLog(`🚀 Lancement — mode : ${currentMode}`);

    if (payload.selectedIds)
        appendLog(`🎯 ${payload.selectedIds.length} trend(s) sélectionné(s)`);
    else
        appendLog("📋 Tous les trends seront traités (liste non chargée)");

    try {
        const response = await fetch(
            `${BASE_URL}/api/trend/start?connectionId=${encodeURIComponent(connectionId)}`,
            {
                method:  "POST",
                headers: { "Content-Type": "application/json" },
                body:    JSON.stringify(payload)
            }
        );

        if (response.status === 401) {
            TokenMonitor.onLogout();
            setTokenExpired();
            showResult(false, { message: "Session expirée (401). Veuillez renouveler le token." });
            return;
        }

        TokenMonitor.registerActivity();

        const result = await response.json();
        showResult(result.success, result);

    } catch (err) {
        appendLog("❌ Erreur réseau : " + err);
        showResult(false, { message: err.toString() });
    } finally {
        setRunning(false);
        setDot("dotAcq", "idle");
    }
}

// ══════════════════════════════════════════
//  Acquisition — Annulation
// ══════════════════════════════════════════
async function cancelAcquisition() {
    const btn = document.getElementById("btnCancel");
    btn.disabled    = true;
    btn.textContent = "⏳ Arrêt…";

    appendLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    appendLog("⛔ Demande d'arrêt envoyée…");

    try {
        const res  = await fetch(`${BASE_URL}/api/trend/cancel`, { method: "POST" });
        const data = await res.json();

        TokenMonitor.registerActivity();

        appendLog(data.success
            ? `⛔ ${data.message}`
            : `⚠️ ${data.message}`);

    } catch (err) {
        appendLog("❌ Erreur lors de l'annulation : " + err);
    } finally {
        btn.disabled    = false;
        btn.textContent = "⛔ Arrêter";
    }
}

// ══════════════════════════════════════════
//  Info projet — chargement initial
// ══════════════════════════════════════════
async function loadProjectInfo() {
    try {
        const res  = await fetch(`${BASE_URL}/api/trend/info`);
        const data = await res.json();

        if (res.ok && data.tokenValid) {
            document.getElementById("headerProjectName").textContent =
                `📁 ${data.projectName}`;
            document.getElementById("lblProjectName").textContent =
                `Projet : ${data.projectName}`;
            document.getElementById("lblTokenStatus").textContent =
                "Token : actif";
            document.getElementById("footerSystemId").textContent =
                `System ID : ${data.systemId}`;

            setDot("dotSession", "ok");
            setDot("dotProject", "ok");
            setDot("dotToken",   "ok");

            appendLog(`✅ Connecté — Projet : ${data.projectName} (ID : ${data.systemId})`);

            if (data.expiresIn) {
                TokenMonitor.onLogin(data.expiresIn);
                appendLog(`⏱️ [TokenMonitor] Surveillance démarrée (expire dans ${data.expiresIn}s)`);
            }

        } else {
            throw new Error(data.error ?? "Token invalide");
        }
    } catch (err) {
        document.getElementById("headerProjectName").textContent = "⚠️ Connexion échouée";
        document.getElementById("lblProjectName").textContent    = "Projet : —";
        document.getElementById("lblTokenStatus").textContent    = "Token : inactif";

        setDot("dotSession", "error");
        setDot("dotProject", "error");
        setDot("dotToken",   "error");

        appendLog(`❌ Erreur de connexion : ${err.message}`);
    }
}

// ══════════════════════════════════════════
//  Refresh Token
// ══════════════════════════════════════════
async function refreshToken() {
    const btn = document.getElementById("btnRefreshToken");
    btn.classList.add("loading");
    btn.textContent = "⏳ Refresh…";

    appendLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    appendLog("🔄 Renouvellement du token en cours…");

    try {
        const res  = await fetch(`${BASE_URL}/api/trend/refresh-token`, { method: "POST" });
        const data = await res.json();

        if (res.ok && data.success) {
            document.getElementById("headerProjectName").textContent =
                `📁 ${data.projectName}`;
            document.getElementById("lblProjectName").textContent =
                `Projet : ${data.projectName}`;
            document.getElementById("lblTokenStatus").textContent =
                `Token renouvelé à ${data.refreshedAt}`;
            document.getElementById("footerSystemId").textContent =
                `System ID : ${data.systemId}`;

            setDot("dotSession", "ok");
            setDot("dotProject", "ok");
            setDot("dotToken",   "ok");

            appendLog(`✅ Token renouvelé avec succès à ${data.refreshedAt}`);

            if (data.expiresIn) {
                TokenMonitor.onLogin(data.expiresIn);
                appendLog(`⏱️ [TokenMonitor] Surveillance redémarrée (expire dans ${data.expiresIn}s)`);
            }

            btn.textContent = "✅ Token OK";
            setTimeout(() => { btn.textContent = "🔄 Refresh Token"; }, 3000);

        } else {
            throw new Error(data.message ?? "Erreur inconnue");
        }
    } catch (err) {
        setDot("dotToken",   "error");
        setDot("dotSession", "error");
        appendLog(`❌ Refresh échoué : ${err.message}`);
        btn.textContent = "❌ Échec";
        setTimeout(() => { btn.textContent = "🔄 Refresh Token"; }, 3000);
    } finally {
        btn.classList.remove("loading");
    }
}

// ══════════════════════════════════════════
//  Status check
// ══════════════════════════════════════════
async function checkStatus() {
    try {
        const r    = await fetch(`${BASE_URL}/api/trend/status`);
        const data = await r.json();

        TokenMonitor.registerActivity();

        setDot("dotAcq", data.isRunning ? "active" : "idle");
    } catch {
        setDot("dotAcq", "error");
    }
}

// ══════════════════════════════════════════
//  Token expiré — mise à jour UI
// ══════════════════════════════════════════
function setTokenExpired() {
    document.getElementById("headerProjectName").textContent =
        "⚠️ Session expirée";
    document.getElementById("lblTokenStatus").textContent =
        "Token : expiré ⚠️";

    setDot("dotToken",   "error");
    setDot("dotSession", "error");

    appendLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    appendLog("⚠️ Token expiré — cliquez sur 🔄 Refresh Token pour vous reconnecter.");
}

// ══════════════════════════════════════════
//  UI Helpers
// ══════════════════════════════════════════
function setRunning(running) {
    document.getElementById("btnStart").disabled  = running;
    document.getElementById("btnCancel").disabled = !running;

    document.getElementById("btnCancel")
        .classList.toggle("si-hidden", !running);

    document.getElementById("badgeRunning")
        .classList.toggle("si-hidden", !running);

    setDot("dotAcq", running ? "active" : "idle");
}

function showResult(success, result) {
    showCard("cardResult");
    const box = document.getElementById("resultBox");

    if (success) {
        box.className = "si-result si-result--success";
        box.innerHTML = `
            <div class="si-result__icon">✅</div>
            <div class="si-result__body">
                <strong>Acquisition terminée avec succès !</strong>
                <ul class="si-result__list">
                    <li>📊 Séries traitées : <b>${result.totalSeries}</b></li>
                    <li>📈 Valeurs extraites : <b>${result.totalValues}</b></li>
                    <li>⏱️ Durée : <b>${result.durationSec?.toFixed(1)} sec</b></li>
                </ul>
            </div>`;
    } else {
        box.className = "si-result si-result--error";
        box.innerHTML = `
            <div class="si-result__icon">⛔</div>
            <div class="si-result__body">
                <strong>${result.message?.includes("annulée") ? "Acquisition annulée" : "Erreur"}</strong>
                <p>${result.message}</p>
                ${result.totalSeries > 0 ? `
                <ul class="si-result__list">
                    <li>📊 Séries traitées : <b>${result.totalSeries}</b></li>
                    <li>📈 Valeurs extraites : <b>${result.totalValues}</b></li>
                    <li>⏱️ Durée : <b>${result.durationSec?.toFixed(1)} sec</b></li>
                </ul>` : ""}
            </div>`;
    }
}

function appendLog(message) {
    const logConsole = document.getElementById("logConsole");
    const line       = document.createElement("div");
    line.className   = "si-log-line";
    line.textContent = `[${new Date().toLocaleTimeString()}] ${message}`;
    logConsole.appendChild(line);
    logConsole.scrollTop = logConsole.scrollHeight;
}

function clearLogs() {
    document.getElementById("logConsole").innerHTML = "";
}

function showCard(id) {
    document.getElementById(id)?.classList.remove("si-hidden");
}

function hideCard(id) {
    document.getElementById(id)?.classList.add("si-hidden");
}

function setDot(id, state) {
    const dot = document.getElementById(id);
    if (!dot) return;
    dot.className = "si-status-dot si-status-dot--" + state;
}

// ══════════════════════════════════════════
//  Init
// ══════════════════════════════════════════
document.addEventListener("DOMContentLoaded", () => {
    initTokenMonitorListeners();
    loadProjectInfo();
    initSignalR();
    setMode("single");
});