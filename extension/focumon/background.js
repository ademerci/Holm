// Posts page status to Holm on this PC only (127.0.0.1). Never answers with data.
const HOLM_URL = "http://127.0.0.1:47631/focumon";
const HEADER = "X-Holm-Focumon";

async function tellHolm(status) {
  try {
    await fetch(HOLM_URL, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        [HEADER]: "1",
      },
      body: JSON.stringify(status),
      cache: "no-store",
    });
  } catch {
    // Holm not running, or the port is busy — try again on the next tick from the page.
  }
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (!message || message.type !== "focumon-status" || !message.status) return;
  tellHolm(message.status).finally(() => sendResponse({ ok: true }));
  return true;
});
