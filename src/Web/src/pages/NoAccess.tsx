import { Link } from "react-router";
import { usePageTitle } from "../layout/usePageTitle";

// For a page the user's role in this tenant doesn't include: the API said 403, or the
// permission is missing from /me. Switching tenants may help, since roles are per tenant.
export default function NoAccess() {
  usePageTitle("No access");

  return (
    <>
      <h1>You don&apos;t have access to this</h1>
      <p className="lead">
        Your role in this tenant doesn&apos;t include this page. If you belong
        to other tenants, your role there may.{" "}
        <Link to="/">Go to the dashboard</Link>.
      </p>
    </>
  );
}
