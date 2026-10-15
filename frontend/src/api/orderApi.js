const API_URL = import.meta.env.VITE_API_URL;

// Structured API error: preserves HTTP status for the UI.
export class ApiError extends Error {
    constructor(message, status = null) {
        super(message);
        this.name = "ApiError";
        this.status = status;
    }
}

async function requestJson(url, options, fallbackMessage) {
    let response;

    try {
        response = await fetch(url, options);
    } catch (error) {
        // No HTTP response: the operation's outcome may be unknown.
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

// CREATE ORDER
export async function createOrder(orderData, idempotencyKey) {
    const token = localStorage.getItem("token");

    if (!token) {
        throw new ApiError("You are not logged in.", 401);
    }

    if (!idempotencyKey) {
        throw new ApiError("Idempotency-Key is required.");
    }

    return requestJson(
        `${API_URL}/api/orders`,
        {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                Authorization: `Bearer ${token}`,
                "Idempotency-Key": idempotencyKey
            },
            body: JSON.stringify(orderData)
        },
        "Order request failed"
    );
}

// GET MY ORDERS
export async function getMyOrders() {
    const token = localStorage.getItem("token");

    if (!token) {
        throw new ApiError("You are not logged in.", 401);
    }

    return requestJson(
        `${API_URL}/api/orders/my-orders`,
        {
            method: "GET",
            headers: {
                Authorization: `Bearer ${token}`
            }
        },
        "Failed to fetch orders"
    );
}

// GET ORDER BY ID
export async function getOrderById(id) {
    const token = localStorage.getItem("token");

    if (!token) {
        throw new ApiError("You are not logged in.", 401);
    }

    return requestJson(
        `${API_URL}/api/orders/${id}`,
        {
            method: "GET",
            headers: {
                Authorization: `Bearer ${token}`
            }
        },
        "Failed to fetch order"
    );
}