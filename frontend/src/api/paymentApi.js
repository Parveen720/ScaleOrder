
import { ApiError } from "./orderApi";

const API_URL = import.meta.env.VITE_API_URL;
const PENDING_PAYMENT_PREFIX = "scaleorder.pendingPayment:";

function getToken() {
  const token = localStorage.getItem("token");

  if (!token) {
    throw new ApiError("You are not logged in.", 401);
  }

  return token;
}

function getPendingPaymentKey(orderId) {
  return `${PENDING_PAYMENT_PREFIX}${orderId}`;
}

async function requestJson(url, options, fallbackMessage) {
  let response;

  try {
    response = await fetch(url, options);
  } catch (error) {
    // Keep the key because the server may have processed the request.
    throw new ApiError(
      `Network error. The server response is unknown. ${error.message}`
    );
  }

  const text = await response.text();
  let data = null;

  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }

  if (!response.ok) {
    const message =
      (typeof data === "object" && data?.message) ||
      (typeof data === "object" && data?.title) ||
      (typeof data === "string" && data) ||
      `${fallbackMessage} (${response.status})`;

    throw new ApiError(message, response.status);
  }

  return data;
}

export async function getMyPayments() {
  const token = getToken();

  const data = await requestJson(
    `${API_URL}/api/payments/my`,
    {
      method: "GET",
      headers: {
        Authorization: `Bearer ${token}`,
      },
    },
    "Failed to fetch payments"
  );

  return Array.isArray(data) ? data : [];
}

export async function createPayment(orderId, simulateFailure = false) {
  if (!orderId) {
    throw new ApiError("A valid Order ID is required.");
  }

  const token = getToken();
  const storageKey = getPendingPaymentKey(orderId);

  let idempotencyKey = localStorage.getItem(storageKey);

  if (!idempotencyKey) {
    idempotencyKey = crypto.randomUUID();

    try {
      localStorage.setItem(storageKey, idempotencyKey);
    } catch {
      throw new ApiError(
        "Could not save the payment retry key. Check browser storage."
      );
    }
  }

  const payment = await requestJson(
    `${API_URL}/api/payments`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify({
        orderId,
        simulateFailure,
      }),
    },
    "Payment request failed"
  );

  // The response is confirmed. A future attempt can get a new key.
  localStorage.removeItem(storageKey);

  return payment;
}
