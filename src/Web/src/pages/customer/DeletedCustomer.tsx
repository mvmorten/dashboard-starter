import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { ApiError } from "../../api";
import { useMe } from "../../auth/useMe";
import {
  currentOf,
  customerFieldLabels,
  customerQueryKey,
  customersQueryKey,
  deletedCustomerQueryKey,
  fieldsOf,
  restoreCustomer,
  type Customer,
  type CustomerField,
} from "../../customers/customers";
import { formatUtc, utcIso } from "../../format";
import History from "./History";
import { messageOf } from "./messages";
import { BackLink, FieldValue } from "./parts";

// A deleted customer, for someone with customers.edit: plainly marked as deleted, its fields
// to read, its history, and Restore. Nothing here can be edited; restore it first.
export default function DeletedCustomer({
  customer,
  onRestored,
}: {
  customer: Customer;
  // Called once the customer is live again, with what to tell the person.
  onRestored: (notice: string) => void;
}) {
  const queryClient = useQueryClient();
  const { data: me } = useMe();
  const tenantId = me?.currentTenantId ?? null;
  const [confirming, setConfirming] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const confirmBox = useRef<HTMLDivElement>(null);
  const fields = fieldsOf(customer);

  // Move focus to the question, so keyboard and screen reader users land on it.
  useEffect(() => {
    if (confirming) {
      confirmBox.current?.focus();
    }
  }, [confirming]);

  // The customer is live: its cached copy is the one the API just returned, the "deleted" copy
  // is gone, and every list is out of date.
  function showLive(live: Customer | null, notice: string) {
    // First, so the form that takes this page's place opens with the notice.
    onRestored(notice);
    if (live) {
      queryClient.setQueryData(customerQueryKey(tenantId, customer.id), live);
    }

    queryClient.removeQueries({
      queryKey: deletedCustomerQueryKey(tenantId, customer.id),
    });
    void queryClient.invalidateQueries({ queryKey: customersQueryKey });
  }

  async function restore() {
    setError(null);
    setPending(true);
    try {
      showLive(
        await restoreCustomer(customer.id, customer.rowVersion),
        "Customer restored.",
      );
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 404) {
        // Not deleted any more: someone else restored it first.
        showLive(null, "Someone else restored this customer already.");
        return;
      }

      const theirs = currentOf(caught);
      if (theirs) {
        // Still deleted, but it changed (restored and deleted again). Show it as it is now.
        queryClient.setQueryData(
          deletedCustomerQueryKey(tenantId, customer.id),
          theirs,
        );
        setConfirming(false);
        setError(
          "This customer changed since you opened it. Check it below, then restore it again.",
        );
        return;
      }

      setError(messageOf(caught));
    } finally {
      setPending(false);
    }
  }

  return (
    <>
      <BackLink />
      <h1>{customer.name}</h1>
      <p className="deleted-banner">
        <strong>Deleted.</strong>{" "}
        {customer.deletedAt && (
          <>
            <time dateTime={utcIso(customer.deletedAt)}>
              {formatUtc(customer.deletedAt)}
            </time>
            {customer.deletedByName && ` by ${customer.deletedByName}`}.{" "}
          </>
        )}
        It isn&apos;t in the list or in search until it&apos;s restored.
      </p>

      <dl className="field-list">
        {(Object.keys(customerFieldLabels) as CustomerField[]).map((field) => (
          <div key={field}>
            <dt>{customerFieldLabels[field]}</dt>
            <dd>
              <FieldValue value={fields[field]} />
            </dd>
          </div>
        ))}
      </dl>

      <div role="alert" className="form-error">
        {error}
      </div>

      {confirming ? (
        <div
          className="restore-section delete-confirm"
          ref={confirmBox}
          tabIndex={-1}
          role="group"
          aria-labelledby="restore-question"
        >
          <p id="restore-question">
            <strong>Restore {customer.name}?</strong> It goes back in the list
            for everyone in this tenant, as it was when it was deleted.
          </p>
          <div className="form-actions">
            <button
              type="button"
              className="primary-button"
              disabled={pending}
              onClick={() => void restore()}
            >
              {pending ? "Restoring..." : "Restore"}
            </button>
            <button
              type="button"
              disabled={pending}
              onClick={() => setConfirming(false)}
            >
              Leave it deleted
            </button>
          </div>
        </div>
      ) : (
        <div className="restore-section">
          <button type="button" onClick={() => setConfirming(true)}>
            Restore customer
          </button>
        </div>
      )}

      <History customerId={customer.id} />
    </>
  );
}
