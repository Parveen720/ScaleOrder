import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { login } from "../api/api";

function Login() {
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");

    const navigate = useNavigate();

    const handleLogin = async (e) => {
        e.preventDefault();

        try {
            const data = await login(email, password);

            console.log("Login successful:", data);

            // Store JWT
            localStorage.setItem("token", data.token);

            // Redirect to dashboard
            navigate("/dashboard");

        } catch (error) {
            console.error(error);
            alert(error.message);
        }
    };

    return (
        <div>
            <h1>ScaleOrder</h1>
            <h2>Login</h2>

            <form onSubmit={handleLogin}>

                <input
                    type="email"
                    placeholder="Email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                />

                <br />

                <input
                    type="password"
                    placeholder="Password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                />

                <br />

                <button type="submit">
                    Login
                </button>

            </form>
        </div>
    );
}

export default Login;