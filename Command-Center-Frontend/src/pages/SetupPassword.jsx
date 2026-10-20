import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api";
import "./PasswordPages.css";

export default function SetupPassword() {
  const [params] = useSearchParams();
  const token = params.get("token") || params.get("code") || "";
  const email = params.get("email") || "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setError(""); setMessage("");
    if (!token) return setError("Password setup token missing. Please open the latest email link.");
    if (password.length < 8) return setError("Password must be at least 8 characters.");
    if (password !== confirm) return setError("Passwords do not match.");

    setBusy(true);
    try {
      await api.post("/auth/set-password", { token, email, password, newPassword: password });
      setMessage("Password set successfully. You can now sign in.");
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || "Could not set password. The link may have expired.");
    } finally { setBusy(false); }
  }

  return <main className="password-page"><form className="password-card" onSubmit={submit}>
    <p className="password-eyebrow">DIXON DIGITAL COMMAND CENTER</p>
    <h1>Set your password</h1>
    <p>Create a password to activate your account.</p>
    <label>New password<input type="password" autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} required minLength={8} /></label>
    <label>Confirm password<input type="password" autoComplete="new-password" value={confirm} onChange={e => setConfirm(e.target.value)} required minLength={8} /></label>
    {error && <p className="password-error">{error}</p>}
    {message && <p className="password-success">{message}</p>}
    <button disabled={busy}>{busy ? "Saving..." : "Set password"}</button>
    <Link to="/">Back to sign in</Link>
  </form></main>;
}
