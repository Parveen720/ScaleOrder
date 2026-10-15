
import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getMyOrders } from "../api/orderApi";
import {
    createPayment,
    getMyPayments
} from "../api/paymentApi";

function Orders() {
    const [orders, setOrders] = useState([]);
    const [payments, setPayments] = useState([]);
    const [loading, setLoading] = useState(true);
    const [paymentsLoading, setPaymentsLoading] = useState(true);
    const [paymentsError, setPaymentsError] = useState("");
    const [ordersError, setOrdersError] = useState("");
    const [payingOrderId, setPayingOrderId] = useState(null);
    const [message, setMessage] = useState("");

    const loadOrders = useCallback(async () => {
        setLoading(true);
        setOrdersError("");

        try {
            const data = await getMyOrders();
            setOrders(Array.isArray(data) ? data : []);
        } catch (error) {
            console.error("Orders loading failed:", error);
            setOrdersError(error.message || "Could not load orders.");
        } finally {
            setLoading(false);
        }
    }, []);

    const loadPayments = useCallback(async () => {
        setPaymentsLoading(true);
        setPaymentsError("");

        try {
            const data = await getMyPayments();
            setPayments(Array.isArray(data) ? data : []);
        } catch (error) {
            console.error("Payment history loading failed:", error);
            setPaymentsError(
                error.message || "Payment history is unavailable."
            );
        } finally {
            setPaymentsLoading(false);
        }
    }, []);

    const refresh = async () => {
        setMessage("");
        await Promise.all([loadOrders(), loadPayments()]);
    };

    useEffect(() => {
        loadOrders();
        loadPayments();
    }, [loadOrders, loadPayments]);

    const handlePay = async (order) => {
        if (payingOrderId) return;

        if (paymentsLoading || paymentsError) {
            setMessage(
                "Payment history must load successfully before paying. " +
                "Fix or refresh PaymentService first."
            );
            return;
        }

        const existingPayment = payments.find(
            (payment) =>
                String(payment.orderId).toLowerCase() ===
                String(order.id).toLowerCase()
        );

        if (existingPayment) {
            setMessage(
                `A payment record already exists. Status: ${existingPayment.status}.`
            );
            return;
        }

        const confirmed = window.confirm(
            `Create a simulated payment for order ${order.id} ` +
            `for ₹${order.totalAmount}? No real money will be charged.`
        );

        if (!confirmed) return;

        setPayingOrderId(order.id);
        setMessage("");

        try {
            const payment = await createPayment(order.id);

            setPayments((current) => [
                payment,
                ...current.filter((item) => item.id !== payment.id)
            ]);

            setMessage(
                `Payment created successfully. Status: ${payment.status}. ` +
                `Payment ID: ${payment.id}`
            );
        } catch (error) {
            console.error("Payment creation failed:", error);

            setMessage(
                `Payment failed: ${error.message}. ` +
                "If the response was lost, retry the same operation rather " +
                "than generating a new idempotency key."
            );

            // Refresh history in case the server saved the payment
            // before the response was lost.
            await loadPayments();
        } finally {
            setPayingOrderId(null);
        }
    };

    return (
        <main style={{
            maxWidth: "900px",
            margin: "30px auto",
            padding: "20px"
        }}>
            <h1>My Orders</h1>

            <Link to="/create-order">
                <button type="button">Create Order</button>
            </Link>

            <button
                type="button"
                onClick={refresh}
                disabled={loading || paymentsLoading}
                style={{ marginLeft: "10px" }}
            >
                Refresh
            </button>

            {message && (
                <p role="status" style={{ color: "#b45309" }}>
                    {message}
                </p>
            )}

            {ordersError && (
                <p role="alert" style={{ color: "crimson" }}>
                    Orders error: {ordersError}
                </p>
            )}

            {paymentsError && (
                <section
                    role="alert"
                    style={{
                        border: "1px solid #dc2626",
                        padding: "12px",
                        marginTop: "16px",
                        borderRadius: "8px"
                    }}
                >
                    <strong>Payments are currently unavailable.</strong>
                    <p>{paymentsError}</p>
                    <p>
                        Your orders are loaded separately. Payment actions
                        remain disabled until payment history is available.
                    </p>
                </section>
            )}

            {loading ? (
                <p>Loading orders...</p>
            ) : orders.length === 0 && !ordersError ? (
                <p>No orders found.</p>
            ) : (
                orders.map((order) => {
                    const payment = payments.find(
                        (item) =>
                            String(item.orderId).toLowerCase() ===
                            String(order.id).toLowerCase()
                    );

                    const isPaying = payingOrderId === order.id;

                    return (
                        <article
                            key={order.id}
                            style={{
                                border: "1px solid #ccc",
                                borderRadius: "8px",
                                padding: "16px",
                                marginTop: "16px"
                            }}
                        >
                            <h3>Order ID: {order.id}</h3>
                            <p>Order status: {order.status}</p>
                            <p>Total: ₹{order.totalAmount}</p>

                            {paymentsLoading ? (
                                <p>Loading payment status...</p>
                            ) : paymentsError ? (
                                <p>Payment status unavailable.</p>
                            ) : payment ? (
                                <>
                                    <p>
                                        Payment status: <strong>{payment.status}</strong>
                                    </p>
                                    <p>Payment ID: {payment.id}</p>
                                    <p>Payment amount: ₹{payment.amount}</p>
                                </>
                            ) : (
                                <p>Payment status: Not initiated</p>
                            )}

                            <button
                                type="button"
                                disabled={
                                    Boolean(payingOrderId) ||
                                    paymentsLoading ||
                                    Boolean(paymentsError) ||
                                    Boolean(payment)
                                }
                                onClick={() => handlePay(order)}
                            >
                                {isPaying
                                    ? "Processing..."
                                    : payment
                                        ? "Payment Recorded"
                                        : "Pay Now"}
                            </button>
                        </article>
                    );
                })
            )}
        </main>
    );
}

export default Orders;
