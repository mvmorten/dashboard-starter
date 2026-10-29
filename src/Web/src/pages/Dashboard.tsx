import { usePageTitle } from "../layout/usePageTitle";

export default function Dashboard() {
  usePageTitle("Dashboard");

  return (
    <>
      <h1>Dashboard</h1>
      <p className="lead">
        KPI cards and charts arrive in a later step. For now this page holds the
        place.
      </p>
    </>
  );
}
