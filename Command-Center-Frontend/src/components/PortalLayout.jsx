import { useEffect, useState } from "react";
import { Link, NavLink, useLocation } from "react-router-dom";
import {
  Activity, AlertTriangle, Bell, ChevronLeft, ChevronRight, X,
  ClipboardList, Factory, FileClock, LayoutDashboard, ListChecks,
  Settings, ShieldCheck, UserRound, ChartNoAxesCombined,
} from "lucide-react";
import Header from "./Header";
import AppFooter from "./AppFooter";
import dixonLogo from "../assets/dixon-logo-white.png";
import { api } from "../api";
import "./PortalLayout.css";

const navigation = {
  contributor: [
    ["/contributor/brands", "Reporting", Factory],
    ["/contributor/dashboard", "Dashboard", LayoutDashboard],
    ["/contributor/reports", "Reports", ClipboardList],
    ["/contributor/activity", "Activity", FileClock],
    ["/notifications", "Notifications", Bell],
  ],
  approver: [
    ["/approvals/overview", "Overview", LayoutDashboard],
    ["/approvals/reports", "Daily reports", ClipboardList],
    ["/approvals", "Review queue", ListChecks],
    ["/approvals/analytics", "Analytics", ChartNoAxesCombined],
    ["/approvals/history", "Activity history", Activity],
    ["/notifications", "Notifications", Bell],
  ],
  admin: [
    ["/admin", "Administration", ShieldCheck],
    ["/dashboard", "Dashboard", LayoutDashboard],
  ],
  superadmin: [
    ["/admin", "Administration", ShieldCheck],
    ["/dashboard", "Dashboard", LayoutDashboard],
  ],
  viewer: [
    ["/viewer", "Overview", LayoutDashboard],
    ["/viewer/exceptions", "Exceptions", ShieldCheck],
    ["/viewer/comparison", "Comparisons", ChartNoAxesCombined],
    ["/viewer/tracker", "Tracker", Activity],
    ["/viewer/reports", "Reports", ClipboardList],
  ],
};

function getNavigation(role) {
  const key = String(role || "").replace(/[^a-z]/gi, "").toLowerCase();
  return navigation[key] || [];
}

function getPendingReportCountsByDate(alerts) {
  const reports = new Map();
  for (const item of alerts) {
    const reportKey = item.submissionBatchId || `legacy-${item.dailyReportId}`;
    if (!reports.has(reportKey)) {
      reports.set(reportKey, String(item.reportDate || "").slice(0, 10));
    }
  }

  const counts = new Map();
  for (const date of reports.values()) {
    const key = date || "unknown";
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  return [...counts.entries()].sort(([dateA], [dateB]) => dateB.localeCompare(dateA));
}

export default function PortalLayout({ user, onSignOut, children, contentClassName = "" }) {
  const [expanded, setExpanded] = useState(() => localStorage.getItem("dcc-sidebar-expanded") === "true");
  const toggleSidebar = () => setExpanded(value => {
    localStorage.setItem("dcc-sidebar-expanded", String(!value));
    return !value;
  });
  const links = [...getNavigation(user?.roleName), ["/profile", "Profile & settings", Settings]];

  return <div className={`portal-layout ${expanded ? "is-expanded" : "is-collapsed"}`}>
    <aside className="portal-sidebar" aria-label={`${user?.roleName || "User"} navigation`}>
      <NavLink className="portal-brand" to={links[0]?.[0] || "/profile"} title="Dixon Command Center">
        <img src={dixonLogo} alt="Dixon" />
        <span>Command Center</span>
      </NavLink>
      <button className="portal-sidebar-toggle" type="button" onClick={toggleSidebar} aria-label={expanded ? "Collapse sidebar" : "Expand sidebar"} aria-expanded={expanded}>
        {expanded ? <ChevronLeft size={16}/> : <ChevronRight size={16}/>}
        <span>{expanded ? "Collapse" : "Expand menu"}</span>
      </button>
      <nav className="portal-navigation">
        {links.map(([to, label, Icon]) => <NavLink key={to} to={to} end={to === "/approvals" || to === "/viewer" || to === "/admin" || to === "/dashboard"} title={label} className={({ isActive }) => `portal-navigation-link${isActive ? " active" : ""}`}>
          <Icon size={18}/><span>{label}</span>
        </NavLink>)}
      </nav>
      <div className="portal-sidebar-user"><span className="portal-avatar">{user?.profilePicture ? <img src={user.profilePicture} alt=""/> : user?.fullName?.slice(0, 1)?.toUpperCase() || <UserRound size={17}/>}</span><div><strong>{user?.fullName || "User"}</strong><small>{user?.roleName || ""}</small></div></div>
    </aside>
    <div className="portal-main-column"><Header user={user} onSignOut={onSignOut}/><WorkspaceAlert user={user}/><main className={`portal-content ${contentClassName}`}>{children}</main><AppFooter user={user}/></div>
  </div>;
}

function WorkspaceAlert({ user }) {
  const location = useLocation();
  const [alerts, setAlerts] = useState([]);
  const role = user?.roleName;
  const [dismissed, setDismissed] = useState(() => sessionStorage.getItem(`dcc-alert-dismissed:${role}`) || "");

  useEffect(() => {
    if (!["Approver", "Contributor"].includes(role)) return undefined;
    let active = true;
    const loadAlerts = async () => {
      try {
        const endpoint = role === "Approver" ? "/approvals/pending" : "/contributor/notifications";
        const result = await api.get(endpoint);
        if (active) setAlerts(Array.isArray(result) ? result : []);
      } catch {
        if (active) setAlerts([]);
      }
    };
    void loadAlerts();
    const timer = window.setInterval(loadAlerts, 120_000);
    return () => { active = false; window.clearInterval(timer); };
  }, [role]);

  const contributorDashboard = role === "Contributor" && location.pathname === "/contributor/dashboard";
  const hasRejected = alerts.some(item => item.type === "REJECTED");
  if (!alerts.length || (contributorDashboard && !hasRejected)) return null;

  const pendingDateCounts = role === "Approver" ? getPendingReportCountsByDate(alerts) : [];
  const pendingReportCount = pendingDateCounts.reduce((total, [, count]) => total + count, 0);
  const pendingReportKeys = role === "Approver"
    ? [...new Set(alerts.map(item => item.submissionBatchId || `legacy-${item.dailyReportId}`))].sort()
    : [];
  const signature = role === "Approver"
    ? pendingReportKeys.join("|")
    : `${alerts.length}:${alerts[0]?.approvalQueueId || alerts[0]?.type || alerts[0]?.reportDate || "active"}`;
  if (dismissed === signature) return null;

  const rejected = role === "Contributor" && hasRejected;
  const overdue = role === "Contributor" && alerts.some(item => item.type !== "REJECTED");
  const urgent = rejected || overdue;
  const rejectedCount = alerts.filter(item => item.type === "REJECTED").length;
  const title = role === "Approver"
    ? `${pendingReportCount} pending report${pendingReportCount === 1 ? "" : "s"} across ${pendingDateCounts.length} date${pendingDateCounts.length === 1 ? "" : "s"}`
    : rejected
      ? `${rejectedCount} submitted report${rejectedCount === 1 ? " needs" : "s need"} your correction`
      : "A daily report is overdue";
  const detail = role === "Approver"
    ? pendingDateCounts.map(([date, count]) => {
        const label = date === "unknown"
          ? "Unknown date"
          : new Date(`${date}T00:00:00`).toLocaleDateString("en-IN", { day: "2-digit", month: "short", year: "numeric" });
        return `${label}: ${count} report${count === 1 ? "" : "s"}`;
      }).join(" · ")
    : alerts.find(item => item.type === "REJECTED")?.message || alerts[0]?.message || "Open your notifications to review the details.";

  return <aside className={`workspace-alert ${rejected ? "is-correction" : overdue ? "is-overdue" : "is-review"}`} role={urgent ? "alert" : "status"}>
    <span className="workspace-alert-icon">{urgent ? <AlertTriangle size={18}/> : <Bell size={18}/>}</span>
    <div className="workspace-alert-copy"><strong>{title}</strong><span>{detail}</span></div>
    <Link to={role === "Approver" ? "/approvals" : "/notifications"}>{role === "Approver" ? "Open review queue" : "View notifications"}</Link>
    <button type="button" aria-label="Dismiss alert" onClick={() => { sessionStorage.setItem(`dcc-alert-dismissed:${role}`, signature); setDismissed(signature); }}><X size={16}/></button>
  </aside>;
}
