import { useState } from "react";
import { useParams } from "react-router";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { useMe } from "../auth/useMe";
import { useCustomer, useDeletedCustomer } from "../customers/useCustomers";
import { usePageTitle } from "../layout/usePageTitle";
import CustomerEditor from "./customer/CustomerEditor";
import CustomerView from "./customer/CustomerView";
import DeletedCustomer from "./customer/DeletedCustomer";
import { BackLink } from "./customer/parts";
import NoAccess from "./NoAccess";
import NotFound from "./NotFound";

// /customers/new and /customers/{id}. With customers.edit, a form; without it, the same fields
// to read. A deleted customer's page (customers.edit only) shows it as deleted, with Restore.
// The API checks every rule again; nothing here is the security boundary.
export default function Customer() {
  const { id } = useParams();
  const { data: me } = useMe();

  if (!can(me, Permissions.customersView)) {
    return <NoAccess />;
  }

  if (id === undefined) {
    return can(me, Permissions.customersEdit) ? (
      <CustomerEditor key="new" initial={null} />
    ) : (
      <NoAccess />
    );
  }

  const customerId = Number(id);
  if (!Number.isInteger(customerId) || customerId < 1) {
    return <NotFound />;
  }

  // A new tenant, or another customer, starts the page over, unsaved edits and all.
  return (
    <ExistingCustomer
      key={`${me?.currentTenantId}-${customerId}`}
      id={customerId}
    />
  );
}

function ExistingCustomer({ id }: { id: number }) {
  const { data: me } = useMe();
  const canEdit = can(me, Permissions.customersEdit);
  const customer = useCustomer(id);
  // GET /api/customers/{id} says 404 for a deleted customer, as it does for one that never
  // existed. Someone who can restore asks the second question: is it a deleted one?
  const missing =
    !customer.data &&
    customer.error instanceof ApiError &&
    customer.error.status === 404;
  const deleted = useDeletedCustomer(id, missing && canEdit);
  // "Customer restored.", kept here in memory, not in history state: this page stays open
  // across a restore (same URL), and a reload must not say it again.
  const [restoredNotice, setRestoredNotice] = useState<string | null>(null);
  usePageTitle(customer.data?.name ?? deleted.data?.name ?? "Customer");

  // Once loaded, the editor keeps its own copy. A later refetch that fails (someone deleted it)
  // must not take the form away from someone typing: the save will say what happened.
  if (customer.data) {
    return canEdit ? (
      <CustomerEditor initial={customer.data} notice={restoredNotice} />
    ) : (
      <CustomerView customer={customer.data} />
    );
  }

  if (customer.isPending) {
    return <p role="status">Loading...</p>;
  }

  if (customer.error instanceof ApiError && customer.error.status === 403) {
    return <NoAccess />;
  }

  if (missing) {
    if (canEdit && deleted.data) {
      return (
        <DeletedCustomer
          customer={deleted.data}
          onRestored={setRestoredNotice}
        />
      );
    }

    if (canEdit && deleted.isPending) {
      return <p role="status">Loading...</p>;
    }

    return (
      <>
        <BackLink />
        <h1>No such customer</h1>
        <p className="lead">
          It may have been deleted, or it belongs to another tenant.
        </p>
      </>
    );
  }

  return (
    <div role="alert">
      <BackLink />
      <h1>Customer</h1>
      <p className="lead">The server didn&apos;t answer.</p>
      <button type="button" onClick={() => void customer.refetch()}>
        Try again
      </button>
    </div>
  );
}
