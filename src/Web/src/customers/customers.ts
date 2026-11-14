// Types and calls for /api/customers (src/Api/Customers/CustomersController.cs). Mirrors
// CustomerModels.cs. The tenant is always the session's current one; no tenant id is sent.

import { ApiError, apiDelete, apiGet, apiPost, apiPut } from "../api";

export type CustomerStatus = "Active" | "Inactive";

export interface CustomerListItem {
  id: number;
  name: string;
  email: string | null;
  phone: string | null;
  status: CustomerStatus;
}

export interface CustomerList {
  items: CustomerListItem[];
  // How many match the search in all, across every page.
  total: number;
  page: number;
  pageSize: number;
}

export interface Customer {
  id: number;
  name: string;
  email: string | null;
  phone: string | null;
  status: CustomerStatus;
  notes: string | null;
  // ISO 8601, UTC, without a zone marker (format.ts adds it).
  createdAt: string;
  updatedAt: string;
  // Who made the last change. Null if nobody did (a data migration).
  updatedByName: string | null;
  // Sent back with a change or a delete, so two people can't overwrite each other (base64).
  rowVersion: string;
}

// The fields a person edits, as the form holds them: always strings, "" for none. The API
// trims them and stores an empty optional field as NULL. A type, not an interface, so it fits
// mergeThreeWay's Record<string, string> (merge.ts).
export type CustomerFields = {
  name: string;
  email: string;
  phone: string;
  status: CustomerStatus;
  notes: string;
};

export type CustomerField = "name" | "email" | "phone" | "status" | "notes";

// In form order, with the labels the form uses (and the merge screen repeats).
export const customerFieldLabels: Record<CustomerField, string> = {
  name: "Name",
  email: "Email",
  phone: "Phone",
  status: "Status",
  notes: "Notes",
};

export const emptyCustomerFields: CustomerFields = {
  name: "",
  email: "",
  phone: "",
  status: "Active",
  notes: "",
};

export function fieldsOf(customer: Customer): CustomerFields {
  return {
    name: customer.name,
    email: customer.email ?? "",
    phone: customer.phone ?? "",
    status: customer.status,
    notes: customer.notes ?? "",
  };
}

// What the API would store: trimmed. Comparing trimmed values means a stray space isn't an
// unsaved change, or a conflict.
export function trimFields(fields: CustomerFields): CustomerFields {
  return {
    name: fields.name.trim(),
    email: fields.email.trim(),
    phone: fields.phone.trim(),
    status: fields.status,
    notes: fields.notes.trim(),
  };
}

export function sameFields(a: CustomerFields, b: CustomerFields): boolean {
  const left = trimFields(a);
  const right = trimFields(b);
  return (Object.keys(customerFieldLabels) as CustomerField[]).every(
    (field) => left[field] === right[field],
  );
}

// Every query key for customers starts with "customers", so one invalidate refreshes them all.
// The tenant is in the key, so a customer from the tenant you just left never shows in the new
// one, even for a moment.
export const customersQueryKey = ["customers"] as const;

export function customerListQueryKey(
  tenantId: number | null,
  search: string,
  page: number,
) {
  return [...customersQueryKey, tenantId, "list", search, page] as const;
}

export function customerQueryKey(tenantId: number | null, id: number) {
  return [...customersQueryKey, tenantId, "one", id] as const;
}

export function fetchCustomers(
  search: string,
  page: number,
  signal: AbortSignal,
): Promise<CustomerList> {
  const query = new URLSearchParams({ page: String(page) });
  if (search) {
    query.set("search", search);
  }

  return apiGet<CustomerList>(`/api/customers?${query}`, { signal });
}

export function fetchCustomer(
  id: number,
  signal: AbortSignal,
): Promise<Customer> {
  return apiGet<Customer>(`/api/customers/${id}`, { signal });
}

export function createCustomer(fields: CustomerFields): Promise<Customer> {
  return apiPost<Customer>("/api/customers", fields);
}

export function updateCustomer(
  id: number,
  fields: CustomerFields,
  rowVersion: string,
): Promise<Customer> {
  return apiPut<Customer>(`/api/customers/${id}`, { ...fields, rowVersion });
}

export function deleteCustomer(id: number, rowVersion: string): Promise<void> {
  return apiDelete<void>(`/api/customers/${id}`, { rowVersion });
}

// A 400's field errors (ValidationProblemDetails.errors, keyed by field), first message each.
export function fieldErrorsOf(
  error: unknown,
): Partial<Record<CustomerField, string>> | null {
  if (!(error instanceof ApiError) || error.status !== 400) {
    return null;
  }

  const errors = (error.body as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== "object") {
    return null;
  }

  const result: Partial<Record<CustomerField, string>> = {};
  for (const field of Object.keys(customerFieldLabels) as CustomerField[]) {
    const messages = (errors as Record<string, unknown>)[field];
    if (Array.isArray(messages) && typeof messages[0] === "string") {
      result[field] = messages[0];
    }
  }

  return result;
}

// A 409's `current` extension: the customer as the other person saved it.
export function currentOf(error: unknown): Customer | null {
  if (!(error instanceof ApiError) || error.status !== 409) {
    return null;
  }

  const current = (error.body as { current?: Customer } | null)?.current;
  return current && typeof current === "object" ? current : null;
}
