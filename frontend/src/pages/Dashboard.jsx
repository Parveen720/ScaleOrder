
import { Link, useNavigate } from "react-router-dom";

function Dashboard() {
    const navigate = useNavigate();

    const logout = () => {
        localStorage.removeItem("token");
        navigate("/login");
    };

    return (
        <div>
            <h1>ScaleOrder Dashboard</h1>
            <p>Welcome to ScaleOrder!</p>

            <Link to="/orders">
                <button type="button">My Orders</button>
            </Link>

            <Link to="/create-order">
                <button type="button" style={{ marginLeft: "10px" }}>
                    Create Order
                </button>
            </Link>

            <button
                type="button"
                onClick={logout}
                style={{ marginLeft: "10px" }}
            >
                Logout
            </button>
        </div>
    );
}

export default Dashboard;
