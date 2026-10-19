// Poruke (dispatch): drivers on the left with unread counts, the conversation on the right.
// Data: api/chat; live: NavigationHub "chatMessage" for the company group.
(function () {
    "use strict";

    const state = { threads: [], driverId: null, messages: [] };
    const el = {
        threads: document.getElementById("chatThreads"),
        summary: document.getElementById("chatThreadsSummary"),
        title: document.getElementById("chatTitle"),
        subtitle: document.getElementById("chatSubtitle"),
        messages: document.getElementById("chatMessages"),
        form: document.getElementById("chatForm"),
        text: document.getElementById("chatText"),
        send: document.getElementById("chatSend"),
    };
    if (!el.threads) return;

    const time = (value) => value ? new Date(value).toLocaleString("sr-Latn-RS", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }) : "";
    const toastError = (text) => window.ProMapCargo?.toast?.error?.(text);

    async function api(url, options) {
        const response = await fetch(url, { headers: { "Content-Type": "application/json" }, credentials: "same-origin", ...options });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        // Some calls (read) answer 200 without a body.
        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    function renderThreads() {
        const unread = state.threads.reduce((sum, t) => sum + t.unread, 0);
        el.summary.textContent = `${state.threads.length} vozača · nepročitano: ${unread}`;
        // The number next to "Poruke" in the menu (services/messages.js).
        document.dispatchEvent(new CustomEvent("promap:messages-unread", { detail: { count: unread } }));
        el.threads.replaceChildren(...state.threads.map((thread) => {
            const item = document.createElement("button");
            item.type = "button";
            item.className = "pm-chat-thread" + (thread.driverId === state.driverId ? " active" : "");
            item.setAttribute("role", "listitem");
            const name = document.createElement("strong");
            name.textContent = thread.driverName;
            const last = document.createElement("span");
            last.className = "pm-text-muted";
            last.textContent = thread.lastText ? `${thread.lastFromDriver ? "" : "Vi: "}${thread.lastText}` : "Nema poruka";
            const meta = document.createElement("span");
            meta.className = "pm-chat-thread-meta";
            meta.textContent = time(thread.lastAt);
            item.append(name, last, meta);
            if (thread.unread > 0) {
                const badge = document.createElement("span");
                badge.className = "pm-nav-count danger";
                badge.textContent = String(thread.unread);
                item.append(badge);
            }
            item.addEventListener("click", () => openThread(thread.driverId));
            return item;
        }));
    }

    function renderMessages() {
        if (!state.driverId) return;
        el.messages.replaceChildren(...state.messages.map((message) => {
            const bubble = document.createElement("div");
            bubble.className = "pm-chat-bubble " + (message.fromDriver ? "from-driver" : "from-dispatch");
            const text = document.createElement("div");
            text.textContent = message.text;
            const meta = document.createElement("div");
            meta.className = "pm-chat-bubble-meta";
            meta.textContent = `${message.fromDriver ? "Vozač" : (message.senderName || "Dispečer")} · ${time(message.createdAt)}${!message.fromDriver && message.readAt ? " · pročitano" : ""}`;
            bubble.append(text, meta);
            return bubble;
        }));
        if (state.messages.length === 0) {
            const empty = document.createElement("p");
            empty.className = "pm-text-muted";
            empty.textContent = "Još nema poruka. Napišite prvu.";
            el.messages.append(empty);
        }
        el.messages.scrollTop = el.messages.scrollHeight;
    }

    async function loadThreads() {
        try {
            state.threads = await api("/api/chat/threads");
            renderThreads();
        } catch {
            el.summary.textContent = "Lista vozača nije učitana.";
        }
    }

    async function openThread(driverId) {
        state.driverId = driverId;
        const thread = state.threads.find((t) => t.driverId === driverId);
        el.title.textContent = thread?.driverName ?? "Vozač";
        el.subtitle.textContent = "Poruke se šalju u aplikaciju vozača.";
        el.text.disabled = false;
        el.send.disabled = false;
        renderThreads();
        try {
            state.messages = await api(`/api/chat/drivers/${driverId}`);
            renderMessages();
            await api(`/api/chat/drivers/${driverId}/read`, { method: "POST" });
            if (thread) thread.unread = 0;
            renderThreads();
        } catch {
            el.messages.textContent = "Razgovor nije učitan.";
        }
        el.text.focus();
    }

    el.form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const text = el.text.value.trim();
        if (!text || !state.driverId) return;
        el.send.disabled = true;
        try {
            await api(`/api/chat/drivers/${state.driverId}`, { method: "POST", body: JSON.stringify({ text }) });
            el.text.value = "";
        } catch {
            toastError("Poruka nije poslata.");
        } finally {
            el.send.disabled = false;
            el.text.focus();
        }
    });

    // Live: a message from any driver (or another dispatcher) of the company.
    function onChatMessage(message) {
        if (message.driverId === state.driverId && !state.messages.some((m) => m.id === message.id)) {
            state.messages.push(message);
            renderMessages();
            if (message.fromDriver) api(`/api/chat/drivers/${state.driverId}/read`, { method: "POST" }).catch(() => {});
        }
        const thread = state.threads.find((t) => t.driverId === message.driverId);
        if (thread) {
            thread.lastText = message.text;
            thread.lastAt = message.createdAt;
            thread.lastFromDriver = message.fromDriver;
            if (message.fromDriver && message.driverId !== state.driverId) thread.unread += 1;
            state.threads.sort((a, b) => (b.unread > 0) - (a.unread > 0) || new Date(b.lastAt || 0) - new Date(a.lastAt || 0));
            renderThreads();
        } else {
            loadThreads();
        }
    }

    function connect() {
        if (!window.signalR) return;
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/navigation-telemetry")
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();
        connection.on("chatMessage", onChatMessage);
        connection.onreconnected(() => connection.invoke("JoinCompany").catch(() => {}));
        connection.start()
            .then(() => connection.invoke("JoinCompany"))
            .catch(() => { el.subtitle.textContent = "Uživo osvežavanje nije dostupno; osvežite stranicu."; });
    }

    loadThreads().then(() => {
        const requested = new URLSearchParams(location.search).get("driver");
        if (requested) openThread(requested);
    });
    if (document.readyState === "complete") connect(); else window.addEventListener("load", connect);
})();
