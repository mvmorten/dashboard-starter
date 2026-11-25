import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { ApiError } from "../../api";
import { currentTenant } from "../../auth/me";
import { useMe } from "../../auth/useMe";
import {
  createCustomer,
  currentOf,
  customerFieldLabels,
  customerQueryKey,
  customersQueryKey,
  deleteCustomer,
  emptyCustomerFields,
  fieldErrorsOf,
  fieldsOf,
  sameFields,
  trimFields,
  updateCustomer,
  type Customer,
  type CustomerField,
  type CustomerFields,
} from "../../customers/customers";
import { mergeThreeWay } from "../../customers/merge";
import {
  customerStatuses,
  notesMaxLength,
  trimLikeServer,
  validateCustomer,
} from "../../customers/rules";
import SelectField from "../../forms/SelectField";
import TextAreaField from "../../forms/TextAreaField";
import TextField from "../../forms/TextField";
import { useForm } from "../../forms/useForm";
import { useArrivalNotice } from "../../layout/useArrivalNotice";
import { usePageTitle } from "../../layout/usePageTitle";
import { useUnsavedChangesWarning } from "../../layout/useUnsavedChangesWarning";
import type { CustomersNotice } from "../Customers";
import DeleteSection from "./DeleteSection";
import History from "./History";
import MergePanel, { type MergeState } from "./MergePanel";
import { listOf, messageOf } from "./messages";
import { BackLink, LastChanged } from "./parts";

// The form for a new customer, or one being edited: saving, the three-way merge after a 409,
// "deleted while you were editing", and delete.
export default function CustomerEditor({
  initial,
  notice: openingNotice = null,
}: {
  initial: Customer | null;
  // Something to say when the form opens that didn't come through navigation: "Customer
  // restored." from the deleted customer's page, which is this same URL.
  notice?: string | null;
}) {
  usePageTitle(initial?.name ?? "New customer");
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: me } = useMe();
  const tenantId = me?.currentTenantId ?? null;
  // "Customer added.", from the form that just created this customer. Read once and removed
  // from history state, so a reload doesn't say it again; and only for the tenant it was
  // written in.
  const [arrived] = useArrivalNotice<CustomersNotice>();
  const arrivedWith =
    arrived && arrived.tenantId === tenantId ? arrived.notice : null;

  // The version these edits build on: what the form loaded, then whatever the last save (or
  // the last 409) returned. Null for a new customer.
  const [base, setBase] = useState<Customer | null>(initial);
  // The fields, their errors, and when errors show (forms/useForm.ts). The client rules are
  // customers/rules.ts, a copy of the server's CustomerValidation.cs.
  const form = useForm<CustomerFields>({
    initial: initial ? fieldsOf(initial) : emptyCustomerFields,
    idPrefix: "customer",
    check: (fields) => validateCustomer(fields).errors,
  });
  const values = form.values;
  const [formError, setFormError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(
    openingNotice ?? arrivedWith,
  );
  const [merge, setMerge] = useState<MergeState | null>(null);
  // Someone deleted it while this person was editing (the save said 404).
  const [gone, setGone] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [pending, setPending] = useState<"save" | "delete" | null>(null);
  const headsUp = useRef<HTMLElement>(null);
  const confirmBox = useRef<HTMLDivElement>(null);

  const unsaved =
    gone || !sameFields(values, base ? fieldsOf(base) : emptyCustomerFields);
  useUnsavedChangesWarning(unsaved && pending === null);

  // Move focus to what just appeared, so keyboard and screen reader users land on it.
  useEffect(() => {
    if (merge || gone) {
      headsUp.current?.focus();
    }
  }, [merge, gone]);

  useEffect(() => {
    if (confirmingDelete) {
      confirmBox.current?.focus();
    }
  }, [confirmingDelete]);

  // The list and this customer's cached copy are out of date after any change.
  function refreshCached(saved: Customer | null) {
    if (saved) {
      queryClient.setQueryData(customerQueryKey(tenantId, saved.id), saved);
    }

    void queryClient.invalidateQueries({ queryKey: customersQueryKey });
  }

  // After a delete, this customer's cached copy must go, not just be marked stale: the page
  // keeps showing a copy it has (so a failed refetch can't take the form away), and opening
  // the customer again from the Deleted list would show the form instead of "Deleted".
  function forgetDeleted(id: number) {
    queryClient.removeQueries({ queryKey: customerQueryKey(tenantId, id) });
    refreshCached(null);
  }

  function showConflict(theirs: Customer, onDelete: boolean) {
    // Each save so far moved `base` forward, so it's the version both edits started from.
    const original = fieldsOf(base!);
    const mine = trimFields(values);
    const result = mergeThreeWay(original, mine, fieldsOf(theirs));

    setBase(theirs);
    form.setValues(result.merged);
    form.clearErrors();
    setConfirmingDelete(false);
    setMerge({
      theirs,
      original,
      mine,
      takenFromTheirs: result.takenFromTheirs,
      conflicts: result.conflicts,
      choices: {},
      onDelete,
    });
    refreshCached(theirs);
  }

  function choose(field: CustomerField, choice: "mine" | "theirs") {
    if (!merge) {
      return;
    }

    setMerge({ ...merge, choices: { ...merge.choices, [field]: choice } });
    form.setField(
      field,
      choice === "mine" ? merge.mine[field] : fieldsOf(merge.theirs)[field],
    );
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // Errors from the last try don't apply any more; this save brings its own, if any.
    form.clearErrors();
    setFormError(null);
    setNotice(null);

    const unchosen = merge?.conflicts.filter((field) => !merge.choices[field]);
    if (unchosen && unchosen.length > 0) {
      setFormError(
        `Choose yours or theirs for ${listOf(unchosen.map((field) => customerFieldLabels[field]))} first.`,
      );
      document.getElementById(`conflict-${unchosen[0]}-mine`)?.focus();
      return;
    }

    if (base && !unsaved) {
      setMerge(null);
      setNotice("Nothing to save: no changes.");
      return;
    }

    // The client rules first, so a mistake shows without a round trip. The server checks every
    // rule again either way.
    if (!form.validate()) {
      return;
    }

    const fields = trimFields(values);
    setPending("save");
    try {
      if (!base) {
        const created = await createCustomer(fields);
        refreshCached(created);
        navigate(`/customers/${created.id}`, {
          replace: true,
          state: {
            notice: "Customer added.",
            tenantId,
          } satisfies CustomersNotice,
        });
        return;
      }

      const saved = await updateCustomer(base.id, fields, base.rowVersion);
      setBase(saved);
      form.setValues(fieldsOf(saved));
      form.clearErrors();
      setMerge(null);
      setNotice("Saved.");
      refreshCached(saved);
    } catch (caught) {
      handleSaveFailure(caught);
    } finally {
      setPending(null);
    }
  }

  function handleSaveFailure(caught: unknown) {
    // The server's own field errors, next to their fields, as if the client rules had found
    // them. With the two sets of rules in step, this only happens if they drift apart.
    const fieldErrors = fieldErrorsOf(caught);
    if (fieldErrors) {
      form.showErrors(fieldErrors);
      return;
    }

    const theirs = currentOf(caught);
    if (theirs) {
      showConflict(theirs, false);
      return;
    }

    if (base && caught instanceof ApiError && caught.status === 404) {
      setMerge(null);
      setGone(true);
      return;
    }

    setFormError(messageOf(caught));
  }

  // Deleted by someone else: this person's version only exists in the form now.
  async function saveAsNew() {
    form.clearErrors();
    setFormError(null);
    if (!form.validate()) {
      setFormError("Some fields need another look.");
      return;
    }

    setPending("save");
    try {
      const created = await createCustomer(trimFields(values));
      refreshCached(created);
      navigate(`/customers/${created.id}`, {
        replace: true,
        state: {
          notice: "Saved as a new customer.",
          tenantId,
        } satisfies CustomersNotice,
      });
    } catch (caught) {
      const fieldErrors = fieldErrorsOf(caught);
      if (fieldErrors) {
        form.showErrors(fieldErrors);
      }

      setFormError(
        fieldErrors ? "Some fields need another look." : messageOf(caught),
      );
    } finally {
      setPending(null);
    }
  }

  async function confirmDelete() {
    if (!base) {
      return;
    }

    setFormError(null);
    setPending("delete");
    try {
      await deleteCustomer(base.id, base.rowVersion);
      forgetDeleted(base.id);
      navigate("/customers", {
        state: {
          notice: `Deleted: ${base.name}`,
          tenantId,
        } satisfies CustomersNotice,
      });
    } catch (caught) {
      const theirs = currentOf(caught);
      if (theirs) {
        showConflict(theirs, true);
        return;
      }

      if (caught instanceof ApiError && caught.status === 404) {
        forgetDeleted(base.id);
        navigate("/customers", {
          state: {
            notice: `${base.name} was already deleted.`,
            tenantId,
          } satisfies CustomersNotice,
        });
        return;
      }

      setFormError(messageOf(caught));
    } finally {
      setPending(null);
    }
  }

  const tenant = me ? currentTenant(me) : null;
  return (
    <>
      <BackLink />
      <h1>{base?.name ?? "New customer"}</h1>
      {base ? (
        <LastChanged customer={base} />
      ) : (
        <p className="lead">
          A new customer in {tenant?.name ?? "this tenant"}.
        </p>
      )}

      <div role="status">{notice && <p className="notice">{notice}</p>}</div>

      {merge && (
        <MergePanel merge={merge} headingRef={headsUp} onChoose={choose} />
      )}

      {gone && (
        <section
          className="heads-up"
          aria-labelledby="gone-heading"
          ref={headsUp}
          tabIndex={-1}
        >
          <h2 id="gone-heading">Someone deleted this customer</h2>
          <p>
            It was deleted while you were editing it, so your changes
            weren&apos;t saved. They&apos;re still in the form below. Save them
            as a new customer, or leave them.
          </p>
        </section>
      )}

      <form method="post" className="customer-form" onSubmit={save} noValidate>
        <TextField
          label={customerFieldLabels.name}
          name="name"
          autoComplete="off"
          {...form.field("name")}
        />
        <TextField
          label={customerFieldLabels.email}
          type="email"
          optional
          name="email"
          autoComplete="off"
          {...form.field("email")}
        />
        <TextField
          label={customerFieldLabels.phone}
          type="tel"
          optional
          name="phone"
          autoComplete="off"
          {...form.field("phone")}
        />
        <SelectField
          label={customerFieldLabels.status}
          options={customerStatuses}
          name="status"
          {...form.field("status")}
        />
        {/* The count uses the trimmed length, because that's what the rule checks. */}
        <TextAreaField
          label={customerFieldLabels.notes}
          optional
          name="notes"
          count={{
            length: trimLikeServer(values.notes).length,
            max: notesMaxLength,
          }}
          {...form.field("notes")}
        />

        <div role="alert" className="form-error">
          {formError}
        </div>

        {gone ? (
          <div className="form-actions">
            <button
              type="button"
              className="primary-button"
              disabled={pending !== null}
              onClick={() => void saveAsNew()}
            >
              {pending === "save" ? "Saving..." : "Save as a new customer"}
            </button>
            <Link to="/customers" className="button-link">
              Leave without saving
            </Link>
          </div>
        ) : (
          <div className="form-actions">
            <button
              type="submit"
              className="primary-button"
              disabled={pending !== null}
            >
              {pending === "save"
                ? "Saving..."
                : base
                  ? merge
                    ? "Save again"
                    : "Save"
                  : "Add customer"}
            </button>
            <Link to="/customers" className="button-link">
              Cancel
            </Link>
          </div>
        )}
      </form>

      {base && !gone && <History customerId={base.id} />}

      {base && !gone && (
        <DeleteSection
          name={base.name}
          unsaved={unsaved}
          confirming={confirmingDelete}
          pending={pending}
          boxRef={confirmBox}
          onStart={() => setConfirmingDelete(true)}
          onCancel={() => setConfirmingDelete(false)}
          onConfirm={() => void confirmDelete()}
        />
      )}
    </>
  );
}
