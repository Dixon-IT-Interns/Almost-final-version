import { useEffect, useRef, useState } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import {
  Activity, BarChart3, Bell, CalendarDays, ChevronDown, ClipboardList,
  Factory, FileClock, Home, LayoutDashboard, ListChecks, LogOut, Menu,
  Settings, ShieldCheck, UserRound, UsersRound, X,
} from "lucide-react";
import { api } from "../api";
import dixonLogoWhite from "../assets/dixon-logo-white.png";
import "./Header.css";

const menuGroups = {
  Contributor: {
    dashboard: "/contributor/dashboard",
    reports: [
      { label: "Daily reporting", description: "Create or continue a daily report", to: "/contributor/brands", icon: Factory },
      { label: "My report history", description: "Track submitted reports and corrections", to: "/contributor/reports", icon: ClipboardList },
      { label: "Workspace activity", description: "View reporting workflow guidance", to: "/contributor/activity", icon: Activity },
    ],
  },
  Approver: {
    dashboard: "/approvals/overview",
    reports: [
      { label: "Daily reports", description: "Browse submitted production by date", to: "/approvals/reports", icon: ClipboardList },
      { label: "Review activity", description: "See report decisions and history", to: "/approvals/history", icon: FileClock },
    ],
    reviews: [
      { label: "Pending reviews", description: "Review complete daily report batches", to: "/approvals", icon: ListChecks },
      { label: "Reviewed reports", description: "Filter approved and correction requested reports", to: "/approvals/reports", icon: ClipboardList },
    ],
    analytics: [
      { label: "Production overview", description: "Plant KPIs and production trends", to: "/approvals/overview", icon: LayoutDashboard },
      { label: "Production analytics", description: "FATP and SMT performance", to: "/approvals/analytics", icon: BarChart3 },
    ],
  },
  Admin: {
    dashboard: "/dashboard",
    administration: [
      { label: "Administration workspace", description: "Manage users and plant configuration", to: "/admin", icon: UsersRound },
      { label: "Account settings", description: "View profile and contact settings", to: "/profile", icon: Settings },
    ],
  },
  SuperAdmin: {
    dashboard: "/dashboard",
    administration: [
      { label: "Administration workspace", description: "Manage plants, users and configuration", to: "/admin", icon: ShieldCheck },
      { label: "Account settings", description: "View profile and contact settings", to: "/profile", icon: Settings },
    ],
  },
  Viewer: {
    dashboard: "/viewer",
    reports: [
      { label: "Report center", description: "Browse read-only production reports", to: "/viewer/reports", icon: ClipboardList },
      { label: "Report tracker", description: "Review daily reporting status", to: "/viewer/tracker", icon: FileClock },
    ],
    analytics: [
      { label: "Production overview", description: "View plant production progress", to: "/viewer", icon: LayoutDashboard },
      { label: "Operations exceptions", description: "Inspect issues and aged work orders", to: "/viewer/exceptions", icon: ShieldCheck },
      { label: "Period comparison", description: "Compare recent production periods", to: "/viewer/comparison", icon: BarChart3 },
    ],
  },
};

const menuDefinitions = [
  ["reports", "Reports", ClipboardList],
  ["reviews", "Review & Approvals", ListChecks],
  ["analytics", "Analytics", BarChart3],
  ["administration", "Administration", Settings],
];

const currentDate = () => new Intl.DateTimeFormat("en-IN", { day: "2-digit", month: "short", year: "numeric" }).format(new Date());

export default function Header({ user, onSignOut }) {
  const navigate = useNavigate();
  const location = useLocation();
  const rootRef = useRef(null);
  const [openMenu, setOpenMenu] = useState("");
  const [accountOpen, setAccountOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notificationCount, setNotificationCount] = useState(0);
  const [date, setDate] = useState(currentDate);
  const groups = menuGroups[user?.roleName] || {};
  const isAdministrator = ["Admin", "SuperAdmin"].includes(user?.roleName);
  const homePath = groups.dashboard || "/";
  const menus = menuDefinitions.filter(([key]) => groups[key]?.length);
  const notificationsAvailable = ["Approver", "Contributor"].includes(user?.roleName);
  const dashboardActive = user?.roleName === "Admin" || user?.roleName === "SuperAdmin"
    ? location.pathname === "/admin" || location.pathname === "/dashboard"
    : location.pathname === homePath;

  useEffect(() => {
    const closeOnOutside = event => {
      if (!rootRef.current?.contains(event.target)) {
        setOpenMenu("");
        setAccountOpen(false);
        setMobileOpen(false);
      }
    };
    const closeOnEscape = event => {
      if (event.key === "Escape") {
        setOpenMenu("");
        setAccountOpen(false);
        setMobileOpen(false);
      }
    };
    document.addEventListener("pointerdown", closeOnOutside);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, []);

  useEffect(() => {
    const timer = window.setInterval(() => setDate(currentDate()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!notificationsAvailable) {
      return undefined;
    }
    let active = true;
    const loadCount = async () => {
      try {
        const path = user.roleName === "Approver" ? "/approvals/pending" : "/contributor/notifications";
        const rows = await api.get(path);
        if (active) setNotificationCount(Array.isArray(rows) ? rows.length : 0);
      } catch {
        if (active) setNotificationCount(0);
      }
    };
    void loadCount();
    const timer = window.setInterval(loadCount, 60_000);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [user?.roleName, notificationsAvailable]);

  const toggleMenu = key => {
    setAccountOpen(false);
    setOpenMenu(current => current === key ? "" : key);
  };

  const signOut = () => {
    setAccountOpen(false);
    onSignOut?.();
    navigate("/");
  };

  return <header className="header" ref={rootRef}>
    <Link className="header-brand" to={homePath} aria-label="Dixon Command Center dashboard">
      <img src={dixonLogoWhite} alt="Dixon" />
      <span className="header-brand-copy">Command Center</span>
    </Link>

    <button className="header-mobile-toggle" type="button" aria-label={mobileOpen ? "Close navigation" : "Open navigation"} aria-expanded={mobileOpen} onClick={() => setMobileOpen(value => !value)}>
      {mobileOpen ? <X size={19}/> : <Menu size={19}/>}
    </button>

    <nav className={`header-navigation ${mobileOpen ? "is-open" : ""}`} aria-label="Main navigation">
      <NavLink to={homePath} end={user?.roleName !== "Admin" && user?.roleName !== "SuperAdmin"} className={`header-dashboard${dashboardActive ? " active" : ""}`} onClick={() => setMobileOpen(false)}>
        <Home size={16}/><span>Dashboard</span>
      </NavLink>
      {menus.map(([key, label, Icon]) => {
        const items = groups[key];
        const MenuIcon = Icon;
        const expanded = openMenu === key;
        return <div className={`header-menu-group ${expanded ? "is-open" : ""}`} key={key}>
          <button className="header-menu-trigger" type="button" aria-haspopup="true" aria-expanded={expanded} aria-controls={`mega-menu-${key}`} onClick={() => toggleMenu(key)} onKeyDown={event => { if (event.key === "ArrowDown" && expanded) document.getElementById(`mega-menu-${key}`)?.querySelector('[role="menuitem"]')?.focus(); }}>
            <MenuIcon size={15}/><span>{label}</span><ChevronDown size={13} className="header-chevron"/>
          </button>
          {expanded && <MegaMenu id={`mega-menu-${key}`} title={label} items={items} onSelect={() => { setOpenMenu(""); setMobileOpen(false); }} />}
        </div>;
      })}
    </nav>

    <div className="header-right">
      <div className="header-date"><CalendarDays size={14}/><span>{date}</span></div>
      {notificationsAvailable && <Link className="header-notifications" to="/notifications" aria-label={notificationCount ? `${notificationCount} active notifications` : "Notifications"} title={notificationCount ? `${notificationCount} active notifications` : "Notifications"}>
        <Bell size={17}/>{notificationCount > 0 && <span className="notification-count">{notificationCount > 99 ? "99+" : notificationCount}</span>}
      </Link>}
      <span className="header-right-separator" aria-hidden="true"/>
      <div className="header-account-wrap">
        <button className="header-account" type="button" aria-haspopup="true" aria-expanded={accountOpen} onClick={() => { setOpenMenu(""); setAccountOpen(value => !value); }}>
          <span className="header-account-avatar">{user?.profilePicture ? <img src={user.profilePicture} alt=""/> : user?.fullName?.slice(0, 1)?.toUpperCase() || <UserRound size={17}/>}</span>
          <span className="header-account-copy"><strong>{user?.fullName || "User"}</strong><small>{user?.roleName || ""}</small></span>
          <ChevronDown size={14} className={`header-account-chevron ${accountOpen ? "is-open" : ""}`}/>
        </button>
        {accountOpen && <div className="header-account-menu" role="menu" aria-label="Account menu">
          <div className="header-account-menu-user"><strong>{user?.fullName || "User"}</strong><span>{user?.roleName}{user?.plantName ? ` · ${user.plantName}` : ""}</span></div>
          <Link role="menuitem" to="/profile" onClick={() => setAccountOpen(false)}><UserRound size={15}/> Profile & settings</Link>
          {isAdministrator && <Link role="menuitem" to="/admin" onClick={() => setAccountOpen(false)}><Settings size={15}/> Administration</Link>}
          <button role="menuitem" type="button" onClick={signOut}><LogOut size={15}/> Sign out</button>
        </div>}
      </div>
    </div>

    {openMenu && <button className="header-menu-backdrop" type="button" tabIndex={-1} aria-label="Close menu" onClick={() => setOpenMenu("")}/>}
  </header>;
}

function MegaMenu({ id, title, items, onSelect }) {
  const handleKeyDown = event => {
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    const items = [...event.currentTarget.querySelectorAll('[role="menuitem"]')];
    if (!items.length) return;
    event.preventDefault();
    const current = items.indexOf(document.activeElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 : event.key === "ArrowDown" ? (current + 1) % items.length : (current <= 0 ? items.length - 1 : current - 1);
    items[next].focus();
  };
  return <section className="header-mega-menu" id={id} role="menu" aria-label={`${title} menu`} onKeyDown={handleKeyDown}>
    <div className="header-mega-heading"><span>WORKSPACE</span><h2>{title}</h2><p>Choose a page to continue.</p></div>
    <div className="header-mega-items">
      {items.map(({ label, description, to, icon: Icon }) => <NavLink key={`${to}-${label}`} role="menuitem" to={to} className={({ isActive }) => `header-mega-item${isActive ? " active" : ""}`} onClick={onSelect}>
        <span className="header-mega-icon"><Icon size={17}/></span><span className="header-mega-copy"><strong>{label}</strong><small>{description}</small></span>
      </NavLink>)}
    </div>
  </section>;
}
