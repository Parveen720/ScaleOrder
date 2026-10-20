
import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getMyOrders } from "../api/orderApi";
import { createPayment, getMyPayments } from "../api/paymentApi";

function getLatestPayment(payments, orderId) {
  return payments
    .filter(
      (payment) =>
        String(payment.orderId).toLowerCase() ===
        String(orderId).toLowerCase()
    )
    .sort(
      (a, b) =>
        new Date(b.createdAt).getTime() -
        new Date(a.createdAt).getTime()
    )[0];
}

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
        "Refresh payment history before attempting payment."
      );
      return;
    }

    const latestPayment = getLatestPayment(payments, order.id);
    const status = String(latestPayment?.status || "").toUpperCase();

    if (status === "SUCCEEDED" || status === "PENDING") {
      setMessage(
        `Payment cannot be retried while the latest status is ${latestPayment.status}.`
      );
      return;
    }

    const confirmed = window.confirm(
      `Create a simulated payment for order ${order.id} for ₹${order.totalAmount}? No real money will be charged.`
    );

    if (!confirmed) return;

    setPayingOrderId(order.id);
    setMessage("");

    try {
      // No client-controlled failure simulation.
      const payment = await createPayment(order.id);

      setMessage(
        `Payment attempt completed. Status: ${payment.status}. Payment ID: ${payment.id}`
      );

      await Promise.all([loadPayments(), loadOrders()]);
    } catch (error) {
      console.error("Payment request failed:", error);

      setMessage(
        `Payment request error: ${error.message}. If the outcome is unknown, refresh payment history before retrying.`
      );

      await loadPayments();
    } finally {
      setPayingOrderId(null);
    }
  };

  return (
    <main
      style={{
        maxWidth: "900px",
        margin: "30px auto",
        padding: "20px",
      }}
    >
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

      {message && <p role="status">{message}</p>}

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
            borderRadius: "8px",
          }}
        >
          <strong>Payments are currently unavailable.</strong>
          <p>{paymentsError}</p>
          <p>
            Refresh payment history before making another payment attempt.
          </p>
        </section>
      )}

      {loading ? (
        <p>Loading orders...</p>
      ) : orders.length === 0 && !ordersError ? (
        <p>No orders found.</p>
      ) : (
        orders.map((order) => {
          const payment = getLatestPayment(payments, order.id);
          const status = String(payment?.status || "").toUpperCase();
          const isPaying = payingOrderId === order.id;

          const canPay =
            !payment ||
            status === "FAILED" ||
            status === "CANCELLED";

          const buttonDisabled =
            Boolean(payingOrderId) ||
            paymentsLoading ||
            Boolean(paymentsError) ||
            !canPay;

          let buttonText = "Pay Now";

          if (isPaying) {
            buttonText = "Processing...";
          } else if (
            status === "FAILED" ||
            status === "CANCELLED"
          ) {
            buttonText = "Pay Again";
          } else if (status === "SUCCEEDED") {
            buttonText = "Payment Successful";
          } else if (status === "PENDING") {
            buttonText = "Payment Pending";
          }

          return (
            <article
              key={order.id}
              style={{
                border: "1px solid #ccc",
                borderRadius: "8px",
                padding: "16px",
                marginTop: "16px",
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
                    Latest payment status:{" "}
                    <strong>{payment.status}</strong>
                  </p>
                  <p>Payment ID: {payment.id}</p>
                  <p>Payment amount: ₹{payment.amount}</p>
                </>
              ) : (
                <p>Payment status: Not initiated</p>
              )}

              <button
                type="button"
                disabled={buttonDisabled}
                onClick={() => handlePay(order)}
              >
                {buttonText}
              </button>
            </article>
          );
        })
      )}
    </main>
  );
}

export default Orders;
