// Nudge Tab Monitor - Background Service Worker
// Connects to Nudge's local WebSocket server and reports active tab changes.

const DEFAULT_PORT = 9123;
const RECONNECT_DELAY_MS = 5000;

let ws = null;
let reconnectTimer = null;
let lastSentData = null;
let isConnected = false;

// --- Badge indicator ---

/**
 * Shows or hides a colored dot badge on the extension icon to indicate
 * whether the current active tab is being tracked by Nudge.
 */
function updateBadge(matched) {
  if (matched) {
    chrome.action.setBadgeText({ text: "●" });
    chrome.action.setBadgeBackgroundColor({ color: "#4CAF50" });
    chrome.action.setTitle({ title: "Nudge: tracking this tab" });
  } else {
    chrome.action.setBadgeText({ text: "" });
    chrome.action.setTitle({ title: "Nudge Tab Monitor" });
  }
}

// --- WebSocket connection management ---

function connect() {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
    return;
  }

  try {
    ws = new WebSocket(`ws://localhost:${DEFAULT_PORT}/`);

    ws.onopen = () => {
      console.log("[Nudge] Connected to Nudge WebSocket server.");
      isConnected = true;
      clearReconnectTimer();
      // Send current tab state immediately on connect
      sendCurrentTabState();
    };

    ws.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        updateBadge(data.matched === true);
      } catch (err) {
        console.log("[Nudge] Failed to parse server message:", err);
      }
    };

    ws.onclose = () => {
      console.log("[Nudge] WebSocket connection closed. Reconnecting...");
      isConnected = false;
      ws = null;
      updateBadge(false);
      scheduleReconnect();
    };

    ws.onerror = (err) => {
      console.log("[Nudge] WebSocket error:", err);
      // onclose will fire after onerror, which handles reconnection
    };
  } catch (err) {
    console.log("[Nudge] Failed to create WebSocket:", err);
    isConnected = false;
    updateBadge(false);
    scheduleReconnect();
  }
}

function scheduleReconnect() {
  clearReconnectTimer();
  reconnectTimer = setTimeout(() => {
    console.log("[Nudge] Attempting reconnection...");
    connect();
  }, RECONNECT_DELAY_MS);
}

function clearReconnectTimer() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
}

function sendMessage(data) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    const json = JSON.stringify(data);

    // Avoid sending duplicate messages
    if (json === lastSentData) {
      return;
    }

    ws.send(json);
    lastSentData = json;
  }
}

// --- Tab tracking ---

/**
 * Queries the current active tab and sends its info to Nudge.
 */
async function sendCurrentTabState() {
  try {
    // chrome.tabs requires the "tabs" permission
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

    if (tab) {
      sendMessage({
        url: tab.url || "",
        title: tab.title || "",
        active: true
      });
    } else {
      // No active tab (e.g., all windows minimized)
      sendMessage({
        url: "",
        title: "",
        active: false
      });
    }
  } catch (err) {
    console.log("[Nudge] Error querying active tab:", err);
  }
}

// Listen for tab activation (user switches tabs)
chrome.tabs.onActivated.addListener((activeInfo) => {
  sendCurrentTabState();
});

// Listen for tab updates (URL or title changes in the active tab)
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  // Only send updates for the active tab, and only when URL or title changes
  if (tab.active && (changeInfo.url || changeInfo.title || changeInfo.status === "complete")) {
    sendMessage({
      url: tab.url || "",
      title: tab.title || "",
      active: true
    });
  }
});

// Listen for window focus changes
chrome.windows.onFocusChanged.addListener((windowId) => {
  if (windowId === chrome.windows.WINDOW_ID_NONE) {
    // Chrome lost focus entirely -- report as inactive
    updateBadge(false);
    sendMessage({
      url: "",
      title: "",
      active: false
    });
  } else {
    // Chrome regained focus -- report the active tab
    sendCurrentTabState();
  }
});

// --- Lifecycle ---

// Connect on service worker startup
connect();

// Keep the service worker alive by responding to alarms
// (Service workers in MV3 can be terminated after ~30s of inactivity)
chrome.alarms.create("nudge-keepalive", { periodInMinutes: 0.25 });
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "nudge-keepalive") {
    // Ensure we're still connected
    if (!isConnected) {
      connect();
    } else {
      // Re-send current state as a heartbeat
      sendCurrentTabState();
    }
  }
});
