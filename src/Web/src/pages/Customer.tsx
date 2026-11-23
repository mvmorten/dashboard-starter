import { useParams } from "react-router";
import { ApiError } from "../api";
import { Permissions, can } from "../auth/permissions";
import { useMe } from "../auth/useMe";
import { useCustomer } from "../customers/useCustomers";
import { usePageTitle } from "../layout/usePageTitle";
import CustomerEditor from "./customer/CustomerEditor";
import CustomerView from "./customer/CustomerView";
import { BackLink } from "./customer/parts";
import NoAccess from "./NoAccess";
import NotFound from "./NotFound";

// /customers/new and /customers/{id}. With customers.edit, a form; without it, the same fields
// to read. The API checks every rule again; nothing here is the security boundary.
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
  const customer = useCustomer(id);
  usePageTitle(customer.data?.name ?? "Customer");

  // Once loaded, the editor keeps its own copy. A later refetch that fails (someone deleted it)
  // must not take the form away from someone typing: the save will say what happened.
  if (customer.data) {
    return can(me, Permissions.customersEdit) ? (
      <CustomerEditor initial={customer.data} />
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

  if (customer.error instanceof ApiError && customer.error.status === 404) {
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
