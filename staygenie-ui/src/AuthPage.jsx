import { useState } from "react";
import "./AuthPage.css";

async function readError(res) {
  try {
    const data = await res.json();
    if (data.message) return data.message;
    if (data.errors) return Object.values(data.errors).flat().join(" ");
  } catch {
    /* ignore */
  }
  return "Something went wrong. Please try again.";
}

export default function AuthPage({ apiBase, onClose, onSuccess }) {
  const [mode, setMode] = useState("login"); // "login" ya "register"
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const isRegister = mode === "register";

  async function submit(e) {
    e.preventDefault();
    setError("");
    setBusy(true);

    const body = isRegister ? { name, email, password } : { email, password };

    try {
      const res = await fetch(`${apiBase}/api/auth/${isRegister ? "register" : "login"}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (!res.ok) {
        setError(await readError(res));
        return;
      }

      const data = await res.json(); // { token, name, role }
      onSuccess({ token: data.token, name: data.name, role: data.role });
    } catch {
      setError("Could not reach the server. Check that the API is running.");
    } finally {
      setBusy(false);
    }
  }

  function switchMode() {
    setMode(isRegister ? "login" : "register");
    setError("");
  }

  return (
    <div className="authOverlay" onClick={onClose}>
      <div className="authCard" onClick={(e) => e.stopPropagation()}>
        <button className="authClose" onClick={onClose} aria-label="Close">
          ✕
        </button>

        <h2>{isRegister ? "Create your account" : "Welcome back"}</h2>
        <p className="authSub">
          {isRegister ? "Register to manage your StayGenie bookings." : "Sign in to your StayGenie account."}
        </p>

        <form onSubmit={submit}>
          {isRegister && (
            <label>
              Name
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                minLength={2}
                maxLength={100}
                autoComplete="name"
              />
            </label>
          )}

          <label>
            Email
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoComplete="email"
            />
          </label>

          <label>
            Password
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              minLength={isRegister ? 8 : undefined}
              autoComplete={isRegister ? "new-password" : "current-password"}
            />
          </label>

          {isRegister && <small className="authHint">At least 8 characters.</small>}
          {error && <p className="authError">{error}</p>}

          <button className="authSubmit" type="submit" disabled={busy}>
            {busy ? "Please wait..." : isRegister ? "Register" : "Sign in"}
          </button>
        </form>

        <p className="authSwitch">
          {isRegister ? "Already have an account?" : "New here?"}{" "}
          <button type="button" onClick={switchMode}>
            {isRegister ? "Sign in" : "Create an account"}
          </button>
        </p>
      </div>
    </div>
  );
}