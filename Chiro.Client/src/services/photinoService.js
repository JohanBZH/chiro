/**
 * Service to handle communication with the Photino .NET backend.
 *
 * Modified to bypass Linux WebKitGTK Photino native injection bugs:
 *   JS → C#:  window.external.sendMessage(string)  [Still works natively]
 *   C# → JS:  HTTP Long-polling to http://127.0.0.1:5174/api/poll [New robust IPC bridge]
 */

/**
 * Initializes the listener for messages coming FROM the C# backend.
 * (Now a no-op as we use fetch instead of event listeners).
 */
export function initPhotinoListener() {
  console.log("[Photino] Initialized HTTP IPC bridge (bypassing native receiveMessage)");
}

/**
 * Sends a message to the Photino .NET backend and waits for the response via local HTTP.
 * @param {string} action - The action name for the backend handler.
 * @param {object} payload - Data to send alongside the action.
 * @param {number} timeoutMs - Max wait time before rejecting (default 120s).
 * @returns {Promise<object>} Resolves with the backend response.
 */
export async function sendMessageToBackend(action, payload, timeoutMs = 120000) {
  try {
    const requestId = crypto.randomUUID();

    const message = JSON.stringify({
      action: action,
      requestId: requestId,
      data: payload || {},
    });

    // 1. Trigger the C# backend action
    if (window.external && typeof window.external.sendMessage === "function") {
      console.log(`[IPC] Sending '${action}' via window.external.sendMessage`);
      window.external.sendMessage(message);
    } else {
      console.warn("[IPC] sendMessage not available — mock mode");
      return {
        status: "error",
        message: "Photino bridge not available — are you running inside Photino?",
        requestId,
      };
    }

    // 2. Poll the C# local HTTP server for the result (Blocking GET)
    console.log(`[IPC] Polling for result of '${action}' (requestId: ${requestId})...`);
    
    // The C# server long-polls for up to 120s internally before returning 408 Timeout.
    const response = await fetch(`http://127.0.0.1:5174/api/poll?requestId=${requestId}`);
    
    if (response.status === 408) {
        throw new Error(`[IPC] Task '${action}' timed out waiting for backend.`);
    }
    if (!response.ok) {
        throw new Error(`[IPC] Server error ${response.status} when polling for '${action}'`);
    }

    const data = await response.json();
    console.log(`[IPC] Received result for '${action}':`, data);
    return data;

  } catch (error) {
    console.error(`[IPC] Error communicating with backend for '${action}':`, error);
    throw error;
  }
}
