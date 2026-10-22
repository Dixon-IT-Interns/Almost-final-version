import PortalLayout from "../PortalLayout";
import "./ApproverShell.css";

export default function ApproverShell({ user, onSignOut, children }) {
  return <PortalLayout user={user} onSignOut={onSignOut} contentClassName="approver-main">{children}</PortalLayout>;
}
