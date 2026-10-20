import { useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import "./PasswordPages.css";

export default function ForgotPassword() {
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setError(""); setMessage(""); setBusy(true);
    try {
      await api.post("/auth/forgot-password", { email });
      setMessage("If an account exists for this email, a password reset link will be sent.");
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || "Unable to request password reset. Please try again.");
    } finally { setBusy(false); }
  }

  return <main className="password-page"><form className="password-card" onSubmit={submit}>
    <p className="password-eyebrow">DIXON DIGITAL COMMAND CENTER</p>
    <h1>Forgot password?</h1>
    <p>Enter your work email to request a password reset link.</p>
    <label>Work email<input type="email" autoComplete="email" value={email} onChange={e => setEmail(e.target.value)} required /></label>
    {error && <p className="password-error">{error}</p>}
    {message && <p className="password-success">{message}</p>}
    <button disabled={busy}>{busy ? "Sending..." : "Send reset link"}</button>
    <Link to="/">Back to sign in</Link>
  </form></main>;
}
