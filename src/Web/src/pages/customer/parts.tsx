import { Link } from "react-router";
import type { Customer } from "../../customers/customers";
import { formatUtc } from "../../format";

// Small pieces the customer page's views share.

export function BackLink() {
  return (
    <p className="back-link">
      <Link to="/customers">All customers</Link>
    </p>
  );
}

export function LastChanged({ customer }: { customer: Customer }) {
  return (
    <p className="lead">
      Last changed {formatUtc(customer.updatedAt)}
      {customer.updatedByName && ` by ${customer.updatedByName}`}.
    </p>
  );
}

export function FieldValue({ value }: { value: string }) {
  return value === "" ? (
    <span className="empty-value">(empty)</span>
  ) : (
    <span className="field-value">{value}</span>
  );
}
