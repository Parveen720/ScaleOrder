
const API_URL = import.meta.env.VITE_API_URL;

async function parseResponse(response) {
    const text = await response.text();

    if (!text) {
        return null;
    }

    try {
        return JSON.parse(text);
    } catch {
        return text;
    }
}

function getErrorMessage(data, fallback) {
    if (typeof data === "object" && data !== null) {
        return data.message || data.title || fallback;
    }

    if (typeof data === "string" && data.trim()) {
        return data;
    }

    return fallback;
}

export async function login(email, password) {
    let response;

    try {
        response = await fetch(`${API_URL}/api/auth/login`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ email, password })
        });
    } catch (error) {
        throw new Error(
            `Cannot connect to the API Gateway. Check that it is running. ${error.message}`
        );
    }

    const data = await parseResponse(response);

    if (!response.ok) {
        throw new Error(
            getErrorMessage(
                data,
                `Login failed (HTTP ${response.status})`
            )
        );
    }

    if (!data || typeof data !== "object") {
        throw new Error(
            `Login endpoint returned an empty or invalid JSON response (HTTP ${response.status}).`
        );
    }

    return data;
}

export async function authenticatedGet(endpoint) {
    const token = localStorage.getItem("token");

    if (!token) {
        throw new Error("You are not logged in.");
    }

    let response;

    try {
        response = await fetch(`${API_URL}${endpoint}`, {
            method: "GET",
            headers: {
                Authorization: `Bearer ${token}`
            }
        });
    } catch (error) {
        throw new Error(
            `Cannot connect to the API Gateway. ${error.message}`
        );
    }

    const data = await parseResponse(response);

    if (!response.ok) {
        throw new Error(
            getErrorMessage(
                data,
                `Request failed (HTTP ${response.status})`
            )
        );
    }

    return data;
}
