/**
 * Service to handle communication with the Photino .NET backend.
 */

// A simple dictionary to keep track of callbacks if we want request/response pattern
const pendingRequests = new Map();

/**
 * Initializes the message listener from the Photino back-end.
 */
export function initPhotinoListener() {
  window.addEventListener("message", (event) => {
    try {
      // Photino sends messages as a string, usually we want to parse it if it's JSON
      const data = JSON.parse(event.data);
      console.log("Received message from Photino backend:", data);

      // Basic handling of request/response
      if (data && data.requestId && pendingRequests.has(data.requestId)) {
        pendingRequests.get(data.requestId)(data);
        pendingRequests.delete(data.requestId);
      }
    } catch (e) {
      console.error("Failed to parse message from Photino:", event.data, e);
    }
  });
}

/**
 * Sends a payload to the Photino .NET host window.
 * @param {string} action The action or command for the backend.
 * @param {any} payload Any data payload to accompany the action.
 * @returns {Promise<any>} A promise that resolves when the C# backend responds (optional).
 */
export function sendMessageToBackend(action, payload) {
  return new Promise((resolve, reject) => {
    try {
      const requestId = crypto.randomUUID();

      // Store the callback
      pendingRequests.set(requestId, resolve);

      const message = {
        action: action,
        requestId: requestId,
        data: payload || {},
      };

      // Photino environment exposes window.external.receiveMessage
      if (window.external && window.external.receiveMessage) {
        window.external.receiveMessage(JSON.stringify(message));
      } else {
        console.warn(
          "Photino window.external.receiveMessage is not available. Running in standard browser?",
        );
        // Simulate a quick mock response if running in a normal browser for development
        setTimeout(() => {
          resolve({
            status: "Mock mode: Backend not attached",
            data: null,
            requestId,
          });
          pendingRequests.delete(requestId);
        }, 500);
      }
    } catch (error) {
      reject(error);
    }
  });
}
