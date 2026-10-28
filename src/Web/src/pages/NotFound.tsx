import { Link } from "react-router";
import { usePageTitle } from "../layout/usePageTitle";

export default function NotFound() {
  usePageTitle("Page not found");

  return (
    <>
      <h1>Page not found</h1>
      <p className="lead">
        There is nothing at this address.{" "}
        <Link to="/">Go to the dashboard</Link>.
      </p>
    </>
  );
}
