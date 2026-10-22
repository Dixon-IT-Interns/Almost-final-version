import PortalLayout from "../PortalLayout";

export default function ContributorLayout({ user, onSignOut, children }) {
  return <PortalLayout user={user} onSignOut={onSignOut} contentClassName="cc-content">{children}</PortalLayout>;
}
