import type { HTMLAttributes, Ref } from "react";
import Field from "./Field";

interface TextFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  // "email" and "tel" bring the right phone keyboard. An email is never capitalized or
  // spell-checked.
  type?: "text" | "email" | "tel";
  name?: string;
  optional?: boolean;
  hint?: string;
  error?: string;
  autoComplete?: string;
  inputMode?: HTMLAttributes<HTMLInputElement>["inputMode"];
  required?: boolean;
  inputRef?: Ref<HTMLInputElement>;
}

// A one-line text input in a Field. No maxLength: it would silently cut pasted text short. The
// rules check the length and say so instead.
export default function TextField({
  id,
  label,
  value,
  onChange,
  type = "text",
  name,
  optional,
  hint,
  error,
  autoComplete,
  inputMode,
  required,
  inputRef,
}: TextFieldProps) {
  const isEmail = type === "email";

  return (
    <Field id={id} label={label} optional={optional} hint={hint} error={error}>
      {(control) => (
        <input
          {...control}
          ref={inputRef}
          name={name}
          type={type}
          autoComplete={autoComplete}
          inputMode={inputMode}
          autoCapitalize={isEmail ? "none" : undefined}
          spellCheck={isEmail ? false : undefined}
          required={required}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  );
}
