import { useEffect, useState } from "react";

// A form's values are strings, one per field, in the order the form shows them. Its errors are
// one message per field that has a problem.
export type FormErrors<V> = Partial<Record<keyof V & string, string>>;

interface FormOptions<V extends Record<string, string>> {
  initial: V;
  // Each input's id is `${idPrefix}-${field}`, so an error can move focus to its field.
  idPrefix: string;
  // The client-side rules: what's wrong with these values. Pure, so it can run on every key.
  check: (values: V) => FormErrors<V>;
}

// The state of one form: its values and the errors it shows. When errors show:
// - Nothing until the first submit, so nobody is told off while still typing.
// - On submit, every problem at once, with focus on the first one.
// - After that, a field showing an error is checked again on every change, so the error goes
//   as soon as it's fixed (or changes to the next problem). Fields without an error stay quiet
//   until the next submit.
// The error text isn't a live region: it's read with its field when the field gets focus, so
// none of this is read out on every key.
export function useForm<V extends Record<string, string>>({
  initial,
  idPrefix,
  check,
}: FormOptions<V>) {
  const [values, setValues] = useState<V>(initial);
  const [errors, setErrors] = useState<FormErrors<V>>({});
  // The field to move focus to once its error is on the page. A new object every time, so
  // submitting twice with the same problem moves focus again.
  const [focusRequest, setFocusRequest] = useState<{ field: string } | null>(
    null,
  );

  // After the render that shows the errors, so the field's aria-describedby already points
  // at its error and a screen reader reads both.
  useEffect(() => {
    if (focusRequest) {
      document.getElementById(`${idPrefix}-${focusRequest.field}`)?.focus();
    }
  }, [focusRequest, idPrefix]);

  function setField(field: keyof V & string, value: string) {
    const next = { ...values, [field]: value } as V;
    setValues(next);

    // Check again only the fields already showing an error. Checking the whole form lets one
    // field fix another's error (typing the password again fixes "don't match").
    const fresh = check(next);
    setErrors((shown) => {
      const kept: FormErrors<V> = {};
      for (const name of Object.keys(shown) as (keyof V & string)[]) {
        if (fresh[name]) {
          kept[name] = fresh[name];
        }
      }

      return kept;
    });
  }

  // Shows these errors (from the client rules, or the server's 400) and moves focus to the
  // first field, in form order. Returns false if there were any.
  function showErrors(found: FormErrors<V>): boolean {
    setErrors(found);
    const first = (Object.keys(values) as (keyof V & string)[]).find(
      (field) => found[field],
    );
    if (first === undefined) {
      return true;
    }

    setFocusRequest({ field: first });
    return false;
  }

  // On submit: true if the values pass the client rules. Otherwise shows why.
  function validate(): boolean {
    return showErrors(check(values));
  }

  // The props a field component needs for one field.
  function field(name: keyof V & string) {
    return {
      id: `${idPrefix}-${name}`,
      value: values[name],
      error: errors[name],
      onChange: (value: string) => setField(name, value),
    };
  }

  return {
    values,
    errors,
    field,
    setField,
    // Replaces every value, without checking: after a save, or when a merge fills the form.
    setValues,
    clearErrors: () => setErrors({}),
    showErrors,
    validate,
  };
}
