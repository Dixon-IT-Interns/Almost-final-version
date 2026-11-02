import { useCallback, useEffect, useState } from "react";
import { Bell, CalendarDays, Clock3, FileText, RefreshCw } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { groupApproverNotificationsByDate } from "../approvalNotifications";
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

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { loadNotifications(); }, [loadNotifications]);

  const isApprover = session.user.roleName === "Approver";
  const displayedNotifications = isApprover
    ? groupApproverNotificationsByDate(notifications)
    : notifications;

  return (
    <PortalLayout user={session.user} onSignOut={onSignOut} contentClassName="notifications-main">
      <div className="notifications-page">
        <div className="notifications-heading">
          <div>
            <span className="notifications-eyebrow">APPROVER WORKSPACE</span>
            <h1>Notifications</h1>
            <p>{isApprover ? "Pending report counts grouped by report date for the customers you review." : "Updates about rejected reports and missing daily reports."}</p>
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
              {displayedNotifications.map((item) => (
                <article className="notification-card" key={isApprover ? item.reportDate : item.approvalQueueId}>
                  <span className="notification-icon"><FileText size={19} /></span>
                  <div className="notification-content">
                    <div className="notification-title-row">
                      <h2>{isApprover ? `${item.count} pending report${item.count === 1 ? "" : "s"}` : item.type === "REJECTED" ? "Your report was rejected" : "Daily report is missing"}</h2>
                      <span className="notification-status">{isApprover ? "Pending review" : item.type === "REJECTED" ? "Action needed" : "Reminder"}</span>
                    </div>
                    {isApprover ? <p>Reports submitted for approval on this date.</p> : <p>{item.message}{item.customerName ? ` (${item.customerName} · ${item.categoryName} · ${item.lineName}${item.modelName ? ` · ${item.modelName}` : ""})` : ""}</p>}
                    <div className="notification-meta">
                      <span><CalendarDays size={14} /> Report date: {item.reportDate === "unknown" ? "Unknown date" : new Date(`${String(item.reportDate).slice(0, 10)}T00:00:00`).toLocaleDateString("en-IN")}</span>
                      {!isApprover && item.submittedAt && <span><Clock3 size={14} /> Submitted: {new Date(item.submittedAt).toLocaleString()}</span>}
                      {!item.submittedAt && item.createdAt && <span><Clock3 size={14} /> Rejected: {new Date(item.createdAt).toLocaleString()}</span>}
                    </div>
                  </div>
                  <Link to={isApprover ? "/approvals" : item.link}>{isApprover ? "Review reports" : item.type === "REJECTED" ? "View reports" : "Create report"}</Link>
                </article>
              ))}
            </section>
          )}
      </div>
    </PortalLayout>
  );
}
