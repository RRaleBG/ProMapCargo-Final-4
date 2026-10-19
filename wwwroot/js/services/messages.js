// Unread messages from drivers on every page: the number next to "Poruke" in the menu and
// in the tab title, plus a short notice when a new message comes (not on the Poruke page,
// which shows it in the conversation). Data: api/chat/unread; live: NavigationHub "chatMessage".
(() => {
    "use strict";
    const root = (window.ProMapCargo = window.ProMapCargo || {});
    const baseTitle = document.title.replace(/^\(\d+\)\s*/, "");
    const onMessagesPage = location.pathname.toLowerCase().startsWith("/messages");
    let count = 0;

    function render(value) {
        count = Math.max(0, Number(value) || 0);
        document.querySelectorAll("[data-message-count]").forEach((el) => {
            el.textContent = count > 99 ? "99+" : String(count);
            el.hidden = count === 0;
            el.setAttribute("aria-label", `${count} nepročitanih poruka`);
        });
        document.title = count > 0 ? `(${count}) ${baseTitle}` : baseTitle;
    }

    async function refresh() {
        try {
            const response = await fetch("/api/chat/unread", { headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (!response.ok) return null;
            const data = await response.json();
            render(data.count);
            return data;
        } catch {
            return null;
        }
    }

    function notify(message) {
        const who = message.senderName || "Vozač";
        const text = message.text.length > 90 ? `${message.text.slice(0, 90)}…` : message.text;
        root.toast?.show?.(`Nova poruka · ${who}: ${text}`, "info");
    }

    function connect() {
        if (!window.signalR) return;
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/navigation-telemetry")
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();
        connection.on("chatMessage", (message) => {
            if (!message?.fromDriver) return;
            // The Poruke page marks the open conversation as read and reports its own total.
            if (!onMessagesPage) {
                render(count + 1);
                notify(message);
            }
        });
        // Back after a dead connection: the count may have changed meanwhile.
        connection.onreconnected(() => { connection.invoke("JoinCompany").catch(() => {}); refresh(); });
        connection.start().then(() => connection.invoke("JoinCompany")).catch(() => {});
    }

    // The Poruke page tells the total after it opens or reads a conversation.
    document.addEventListener("promap:messages-unread", (event) => render(event.detail?.count));
    root.messages = { refresh, render };

    refresh().then((data) => {
        // Only dispatch gets drivers' messages live; drivers see theirs in the app.
        if (data?.dispatch) {
            if (document.readyState === "complete") connect(); else window.addEventListener("load", connect);
        }
    });
})();
