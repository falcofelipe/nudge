// Nudge Tab Monitor - Popup Script
// Shows connection status and current active tab info.

const PORT = 9123;

async function updateStatus() {
  const dot = document.getElementById("statusDot");
  const text = document.getElementById("statusText");
  const tabInfoDiv = document.getElementById("tabInfo");
  const tabTitle = document.getElementById("tabTitle");

  try {
    // Check if the Nudge WebSocket server is reachable by making a simple HTTP request
    const response = await fetch(`http://localhost:${PORT}/`, {
      method: "GET",
      signal: AbortSignal.timeout(2000)
    });

    if (response.ok) {
      dot.className = "dot connected";
      text.textContent = "Connected to Nudge";
    } else {
      dot.className = "dot disconnected";
      text.textContent = "Nudge not responding";
    }
  } catch (err) {
    dot.className = "dot disconnected";
    text.textContent = "Nudge not reachable";
  }

  // Show current active tab info
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab && tab.title) {
      tabInfoDiv.style.display = "block";
      tabTitle.textContent = tab.title;
    }
  } catch (err) {
    // Ignore
  }
}

updateStatus();
