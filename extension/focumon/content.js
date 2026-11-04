// Watches the Focumon page and asks the background worker to tell Holm.
// Content scripts are isolated from the page — FocumonReaders is NOT on window.
// Debug from the page console with:
//   document.documentElement.dispatchEvent(new Event('holm-focumon-debug'))
(() => {
  const INTERVAL_MS = 1000;
  const VERSION = chrome.runtime.getManifest().version;
  let lastJson = "";
  let timer = 0;

  document.documentElement.dataset.holmFocumon = VERSION;
  console.info("Focumon for Holm: content script " + VERSION + " on", location.href);

  function snapshot() {
    try {
      return globalThis.FocumonReaders.read();
    } catch (err) {
      console.warn("Focumon for Holm: read failed", err);
      return null;
    }
  }

  function publish(forceGone) {
    const status = forceGone ? { v: 1, phase: "gone" } : snapshot();
    if (!status) return;
    const json = JSON.stringify(status);
    if (!forceGone && json === lastJson) return;
    lastJson = forceGone ? "" : json;
    chrome.runtime.sendMessage({ type: "focumon-status", status }).catch(() => {});
  }

  function start() {
    publish(false);
    if (timer) clearInterval(timer);
    timer = setInterval(() => publish(false), INTERVAL_MS);
  }

  function writeDebug(report) {
    const json = JSON.stringify(report, null, 2);
    document.documentElement.dataset.holmFocumonDebug = json;
    let tag = document.getElementById("holm-focumon-debug-json");
    if (!tag) {
      tag = document.createElement("script");
      tag.id = "holm-focumon-debug-json";
      tag.type = "application/json";
      document.documentElement.appendChild(tag);
    }
    tag.textContent = json;
    // One string so it always shows even if object preview is collapsed.
    console.log("Focumon for Holm debug JSON:\n" + json);
  }

  document.documentElement.addEventListener("holm-focumon-debug", () => {
    try {
      const report = globalThis.FocumonReaders.debug
        ? globalThis.FocumonReaders.debug()
        : { status: snapshot(), note: "no debug()" };
      writeDebug(report);
    } catch (err) {
      console.warn("Focumon for Holm: debug failed", err);
      writeDebug({ error: String(err && err.message ? err.message : err) });
    }
  });

  const observer = new MutationObserver(() => publish(false));
  observer.observe(document.documentElement, {
    subtree: true,
    childList: true,
    characterData: true,
    attributes: true,
  });

  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "hidden") return;
    publish(false);
  });
  window.addEventListener("pagehide", () => publish(true));

  start();
})();
