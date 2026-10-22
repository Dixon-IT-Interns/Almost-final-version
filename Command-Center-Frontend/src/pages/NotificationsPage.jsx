import { useCallback, useEffect, useState } from "react";
import { Bell, CalendarDays, Clock3, FileText, RefreshCw } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../api";
import PortalLayout from "../components/PortalLayout";
import "./NotificationsPage.css";

export default function NotificationsPage({ session, onSignOut }) {
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const loadNotifications = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const path = session.user.roleName === "Approver" ? "/approvals/pending" : "/contributor/notifications";
      setNotifications(await api.get(path));
    } catch (requestError) {
      setError(requestError?.message || "Unable to load notifications.");
    } finally {
      setLoading(false);
    }
  }, [session.user.roleName]);

  useEffect(() => { loadNotifications(); }, [loadNotifications]);

  return (
    <PortalLayout user={session.user} onSignOut={onSignOut} contentClassName="notifications-main">
      <div className="notifications-page">
        <div className="notifications-heading">
          <div>
            <span className="notifications-eyebrow">APPROVER WORKSPACE</span>
            <h1>Notifications</h1>
            <p>{session.user.roleName === "Approver" ? "New report submissions for the customers you review." : "Updates about rejected reports and missing daily reports."}</p>
          </div>
          <button type="button" onClick={loadNotifications} disabled={loading}>
            <RefreshCw size={15} className={loading ? "notifications-spin" : ""} />
            {loading ? "Refreshing…" : "Refresh"}
          </button>
        </div>

        {error && <div className="notifications-error" role="alert">{error}</div>}
        {loading ? <div className="notifications-empty">Loading notifications…</div> :
          notifications.length === 0 ? (
            <section className="notifications-empty-card">
              <span><Bell size={22} /></span>
              <h2>You’re all caught up</h2>
              <p>{session.user.roleName === "Approver" ? "New contributor submissions will appear here until they are reviewed." : "You’re up to date. Rejections and overdue reports will appear here."}</p>
            </section>
          ) : (
            <section className="notifications-list" aria-label="Pending report notifications">
              {notifications.map((item) => (
                <article className="notification-card" key={item.approvalQueueId}>
                  <span className="notification-icon"><FileText size={19} /></span>
                  <div className="notification-content">
                    <div className="notification-title-row">
                      <h2>{session.user.roleName === "Approver" ? `${item.submittedBy} submitted a report` : item.type === "REJECTED" ? "Your report was rejected" : "Daily report is missing"}</h2>
                      <span className="notification-status">{session.user.roleName === "Approver" ? "Pending review" : item.type === "REJECTED" ? "Action needed" : "Reminder"}</span>
                    </div>
                    {session.user.roleName === "Approver" ? <p>{item.customerName} · {item.categoryName} · {item.lineName}{item.modelName ? ` · ${item.modelName}` : ""}</p> : <p>{item.message}{item.customerName ? ` (${item.customerName} · ${item.categoryName} · ${item.lineName}${item.modelName ? ` · ${item.modelName}` : ""})` : ""}</p>}
                    <div className="notification-meta">
                      <span><CalendarDays size={14} /> Report date: {new Date(item.reportDate).toLocaleDateString()}</span>
                      {item.submittedAt && <span><Clock3 size={14} /> Submitted: {new Date(item.submittedAt).toLocaleString()}</span>}
                      {!item.submittedAt && item.createdAt && <span><Clock3 size={14} /> Rejected: {new Date(item.createdAt).toLocaleString()}</span>}
                    </div>
                  </div>
                  <Link to={session.user.roleName === "Approver" ? "/approvals" : item.link}>{session.user.roleName === "Approver" ? "Review report" : item.type === "REJECTED" ? "View reports" : "Create report"}</Link>
                </article>
              ))}
            </section>
          )}
      </div>
    </PortalLayout>
  );
}
