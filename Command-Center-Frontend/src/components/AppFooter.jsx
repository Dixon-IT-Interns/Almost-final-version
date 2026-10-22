import { ArrowUpRight, Factory, ShieldCheck } from "lucide-react";
import { Link } from "react-router-dom";
import dixonLogoWhite from "../assets/dixon-logo-white.png";
import "./AppFooter.css";

const workspaceLinks = {
  Contributor: [
    { label: "Dashboard", to: "/contributor/dashboard" },
    { label: "Start a daily report", to: "/contributor/brands" },
    { label: "Submitted reports", to: "/contributor/reports" },
    { label: "Activity", to: "/contributor/activity" },
    { label: "Notifications", to: "/notifications" },
  ],
  Approver: [
    { label: "Overview", to: "/approvals/overview" },
    { label: "Daily reports", to: "/approvals/reports" },
    { label: "Review queue", to: "/approvals" },
    { label: "Review history", to: "/approvals/history" },
    { label: "Notifications", to: "/notifications" },
  ],
  Admin: [
    { label: "Dashboard", to: "/dashboard" },
    { label: "Administration", to: "/admin" },
  ],
  SuperAdmin: [
    { label: "Dashboard", to: "/dashboard" },
    { label: "Administration", to: "/admin" },
  ],
  Viewer: [
    { label: "Overview", to: "/viewer" },
    { label: "Exceptions", to: "/viewer/exceptions" },
    { label: "Report tracker", to: "/viewer/tracker" },
    { label: "Report center", to: "/viewer/reports" },
  ],
};

export default function AppFooter({ user }) {
  const links = [...(workspaceLinks[user?.roleName] ?? []), { label: "Profile & settings", to: "/profile" }];

  return (
    <footer className="app-footer">
      <div className="app-footer-inner">
        <section className="app-footer-brand" aria-label="Dixon Command Center">
          <div className="app-footer-brand-panel">
            <img src={dixonLogoWhite} alt="Dixon" />
            <span>Command Center</span>
          </div>
          <div className="app-footer-brand-copy">
            <span className="app-footer-kicker"><span /> FACTORY OPERATIONS</span>
            <h2>Dixon <strong>Command Center</strong></h2>
            <p>Daily KPI reporting, production visibility and accountable approvals for Dixon factory teams.</p>
          </div>
        </section>

        <nav className="app-footer-nav" aria-label="Workspace navigation">
          <div className="app-footer-heading"><span>Your workspace</span><strong>{user?.roleName || "Factory operations"}</strong></div>
          {links.map(({ label, to }) => <Link key={to} to={to}><span>{label}</span><ArrowUpRight size={15} aria-hidden="true" /></Link>)}
        </nav>

        <section className="app-footer-context" aria-label="Current workspace">
          <div className="app-footer-context-label"><Factory size={15} aria-hidden="true" /> CURRENT PLANT</div>
          <strong>{user?.plantName || "Dixon factory workspace"}</strong>
          <div className="app-footer-session"><ShieldCheck size={14} aria-hidden="true" /> Internal workspace</div>
        </section>
      </div>
      <div className="app-footer-bottom">
        <span>Factory KPI reporting · Approvals · Administration</span>
        <span>© {new Date().getFullYear()} Dixon Command Center</span>
      </div>
    </footer>
  );
}
