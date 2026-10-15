import { useState } from "react";
import { createOrder, ApiError } from "../api/orderApi";
import { useNavigate } from "react-router-dom";

const PENDING_ORDER_KEY = "scaleorder.pendingOrder";

function readPendingOrder() {
    try {
        const saved = localStorage.getItem(PENDING_ORDER_KEY);

        if (!saved) return null;

        const operation = JSON.parse(saved);

        if (
            typeof operation.idempotencyKey === "string" &&
            operation.orderData?.items?.length > 0
        ) {
            return operation;
        }
    } catch (error) {
        console.error("Could not restore pending order:", error);
    }

    return null;
}

function CreateOrder() {
    const navigate = useNavigate();

    const [pendingOperation, setPendingOperation] =
        useState(readPendingOrder);

    const [productId, setProductId] = useState(
        () => readPendingOrder()?.orderData?.items?.[0]?.productId ?? ""
    );

    const [quantity, setQuantity] = useState(
        () => readPendingOrder()?.orderData?.items?.[0]?.quantity ?? 1
    );

    const [unitPrice, setUnitPrice] = useState(
        () => readPendingOrder()?.orderData?.items?.[0]?.unitPrice ?? ""
    );

    const [isSubmitting, setIsSubmitting] = useState(false);
    const [errorMessage, setErrorMessage] = useState("");

    const handleSubmit = async (e) => {
        e.preventDefault();

        if (isSubmitting) return;

        setErrorMessage("");

        let operation = pendingOperation;

        // Create and persist a key only for a genuinely new operation.
        if (!operation) {
            const normalizedProductId = productId.trim();
            const numericQuantity = Number(quantity);
            const numericPrice = Number(unitPrice);

            if (
                !normalizedProductId ||
                !Number.isInteger(numericQuantity) ||
                numericQuantity < 1 ||
                !Number.isFinite(numericPrice) ||
                numericPrice <= 0
            ) {
                setErrorMessage(
                    "Enter a valid Product ID, quantity and unit price."
                );
                return;
            }

            operation = {
                idempotencyKey: crypto.randomUUID(),
                orderData: {
                    items: [
                        {
                            productId: normalizedProductId,
                            quantity: numericQuantity,
                            unitPrice: numericPrice
                        }
                    ]
                }
            };

            try {
                // Save BEFORE sending the request. A reload or timeout
                // must not silently generate a new key for this operation.
                localStorage.setItem(
                    PENDING_ORDER_KEY,
                    JSON.stringify(operation)
                );
                setPendingOperation(operation);
            } catch {
                setErrorMessage(
                    "Could not save the pending order safely. " +
                    "Check browser storage and try again."
                );
                return;
            }
        }

        setIsSubmitting(true);

        try {
            const result = await createOrder(
                operation.orderData,
                operation.idempotencyKey
            );

            // Only clear the pending operation after confirmed success.
            localStorage.removeItem(PENDING_ORDER_KEY);
            setPendingOperation(null);

            alert(`Order created successfully! Order ID: ${result.id}`);
            navigate("/orders");
        } catch (error) {
            console.error("Order request failed:", error);

            if (error instanceof ApiError && error.status === 409) {
                setErrorMessage(
                    "This key belongs to a different request. " +
                    "Do not retry with a new key automatically. " +
                    "Review the pending operation before starting another order."
                );
            } else if (!error.status) {
                setErrorMessage(
                    "The server response could not be confirmed. " +
                    "Retry this same operation to avoid creating a duplicate."
                );
            } else {
                setErrorMessage(error.message);
            }

            // Keep the pending key and original payload on failure.
        } finally {
            setIsSubmitting(false);
        }
    };

    const abandonPendingOperation = () => {
        const confirmed = window.confirm(
            "Abandon this pending operation? If the server already " +
            "created the order but its response was lost, creating a " +
            "new operation may create a duplicate. Continue only if " +
            "you have checked the order status."
        );

        if (!confirmed) return;

        localStorage.removeItem(PENDING_ORDER_KEY);
        setPendingOperation(null);
        setErrorMessage("");
    };

    return (
        <div>
            <h1>Create Order</h1>

            {pendingOperation && (
                <p role="status">
                    A pending order operation was restored. Retrying will
                    reuse its original key and payload. Review your orders
                    before abandoning it.
                </p>
            )}

            {errorMessage && (
                <p role="alert" style={{ color: "crimson" }}>
                    {errorMessage}
                </p>
            )}

            <form onSubmit={handleSubmit}>
                <div>
                    <label>Product ID</label>
                    <br />
                    <input
                        type="text"
                        value={productId}
                        onChange={(e) => setProductId(e.target.value)}
                        required
                        disabled={isSubmitting || !!pendingOperation}
                    />
                </div>

                <br />

                <div>
                    <label>Quantity</label>
                    <br />
                    <input
                        type="number"
                        min="1"
                        step="1"
                        value={quantity}
                        onChange={(e) => setQuantity(e.target.value)}
                        required
                        disabled={isSubmitting || !!pendingOperation}
                    />
                </div>

                <br />

                <div>
                    <label>Unit Price</label>
                    <br />
                    <input
                        type="number"
                        min="0.01"
                        step="0.01"
                        value={unitPrice}
                        onChange={(e) => setUnitPrice(e.target.value)}
                        required
                        disabled={isSubmitting || !!pendingOperation}
                    />
                </div>

                <br />

                <button type="submit" disabled={isSubmitting}>
                    {isSubmitting
                        ? "Submitting..."
                        : pendingOperation
                            ? "Retry Pending Order"
                            : "Create Order"}
                </button>
            </form>

            {pendingOperation && !isSubmitting && (
                <button
                    type="button"
                    onClick={abandonPendingOperation}
                >
                    Abandon Pending Operation
                </button>
            )}

            <br />
            <button
                type="button"
                onClick={() => navigate("/orders")}
                disabled={isSubmitting}
            >
                View My Orders
            </button>
        </div>
    );
}

export default CreateOrder;