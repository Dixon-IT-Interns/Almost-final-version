import { useEffect, useState } from "react";
import { Camera, Check, RefreshCw, Send, ShieldCheck, Trash2, UserRound } from "lucide-react";
import { api } from "../api";
import PortalLayout from "../components/PortalLayout";
import "./ProfilePage.css";

export default function ProfilePage({ session, onSignOut, onProfileUpdate }) {
  const [profile, setProfile] = useState(session.user);
  const [pendingPhoneNumber, setPendingPhoneNumber] = useState("");
  const [otp, setOtp] = useState("");
  const [otpSent, setOtpSent] = useState(false);
  const [preview, setPreview] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = async () => {
    setLoading(true);
    setError("");
    try {
      const result = await api.get("/profile");
      setProfile(result);
      onProfileUpdate?.(result);
    } catch (requestError) {
      setError(requestError?.message || "Unable to load profile.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const sendPhoneOtp = async (event) => {
    event.preventDefault();
    setBusy(true); setError(""); setNotice("");
    try {
      const result = await api.post("/profile/phone/otp", { phoneNumber: pendingPhoneNumber.trim() });
      setOtpSent(true); setOtp("");
      setNotice(result.message || "Verification code sent.");
    } catch (requestError) {
      setError(requestError?.message || "Unable to send the verification code.");
      setOtpSent(false);
    } finally {
      setBusy(false);
    }
  };

  const verifyPhoneOtp = async (event) => {
    event.preventDefault();
    setBusy(true); setError(""); setNotice("");
    try {
      const result = await api.post("/profile/phone/verify", { code: otp.trim() });
      setPendingPhoneNumber(""); setOtp(""); setOtpSent(false);
      await load();
      setNotice(result.message || "Phone number verified and saved.");
    } catch (requestError) {
      setError(requestError?.message || "Unable to verify the code.");
    } finally { setBusy(false); }
  };

  const uploadPicture = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;
    if (file.size > 1024 * 1024) { setError("Profile pictures must be 1 MB or smaller."); return; }
    setPreview(URL.createObjectURL(file));
    setBusy(true); setError(""); setNotice("");
    try {
      const form = new FormData();
      form.append("picture", file);
      await api.postFormData("/profile/picture", form);
      await load();
      setPreview("");
      setNotice("Profile picture updated.");
    } catch (requestError) {
      setPreview("");
      setError(requestError?.message || "Unable to upload profile picture.");
    } finally {
      setBusy(false);
      event.target.value = "";
    }
  };

  const removePicture = async () => {
    setBusy(true); setError(""); setNotice("");
    try {
      await api.delete("/profile/picture");
      await load();
      setNotice("Profile picture removed.");
    } catch (requestError) {
      setError(requestError?.message || "Unable to remove profile picture.");
    } finally { setBusy(false); }
  };

  const image = preview || profile.profilePicture;

  return <PortalLayout user={profile} onSignOut={onSignOut} contentClassName="profile-main">
    <div className="profile-page">
      <div className="profile-page-heading"><span>ACCOUNT SETTINGS</span><h1>Your profile</h1><p>Update your contact details and profile photo. Your role and plant are managed by your administrator.</p></div>
      {error && <div className="profile-message error" role="alert">{error}</div>}
      {notice && <div className="profile-message success" role="status"><Check size={15}/>{notice}</div>}
      {loading ? <div className="profile-loading"><RefreshCw size={17}/> Loading profile…</div> : <div className="profile-card">
        <section className="profile-photo-section">
          <div className="profile-avatar-large">{image ? <img src={image} alt="Profile" /> : <UserRound size={36}/>}</div>
          <div className="profile-photo-copy"><strong>Profile photo</strong><span>JPEG, PNG or WebP · up to 1 MB</span><div className="profile-photo-actions"><label className="profile-upload-button"><Camera size={15}/>{busy ? "Please wait…" : "Upload photo"}<input type="file" accept="image/jpeg,image/png,image/webp" onChange={uploadPicture} disabled={busy}/></label>{profile.profilePicture && <button type="button" className="profile-remove-button" onClick={removePicture} disabled={busy}><Trash2 size={14}/> Remove</button>}</div></div>
        </section>
        <form className="profile-form" onSubmit={sendPhoneOtp}>
          <div className="profile-section-title"><h2>Personal information</h2><p>Your role and plant details are read-only.</p></div>
          <label>Full name<input value={profile.fullName || ""} readOnly /></label>
          <label>Email address<input value={profile.email || ""} readOnly /></label>
          <div className="phone-verification-section">
            <div className="phone-verified"><ShieldCheck size={16}/><span>Verified phone</span><strong>{profile.phoneNumber || "No phone number added"}</strong></div>
            <label>New phone number<input type="tel" maxLength={30} value={pendingPhoneNumber} onChange={event=>{setPendingPhoneNumber(event.target.value);setOtpSent(false);}} placeholder="+91 98765 43210" autoComplete="tel" /><small>Include country code. Number is saved after OTP verification.</small></label>
            {!otpSent ? <button type="submit" className="profile-save-button" disabled={busy || !pendingPhoneNumber.trim()}><Send size={15}/>{busy ? "Sending code…" : "Send verification code"}</button> : <div className="phone-otp-step"><label>6-digit code<input type="text" inputMode="numeric" autoComplete="one-time-code" maxLength={6} pattern="[0-9]{6}" value={otp} onChange={event=>setOtp(event.target.value.replace(/\D/g, "").slice(0, 6))} placeholder="Enter OTP" required /></label><div className="phone-otp-actions"><button type="button" className="profile-secondary-button" disabled={busy} onClick={sendPhoneOtp}>Resend code</button><button type="button" className="profile-save-button" disabled={busy || otp.length !== 6} onClick={verifyPhoneOtp}><Check size={15}/>{busy ? "Verifying…" : "Verify and save"}</button></div></div>}
          </div>
          <div className="profile-readonly-grid"><div><span>Role</span><strong>{profile.roleName || "—"}</strong></div><div><span>Plant</span><strong>{profile.plantName || "—"}</strong></div></div>
        </form>
      </div>}
    </div>
  </PortalLayout>;
}
