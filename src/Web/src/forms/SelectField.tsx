import Field from "./Field";

interface SelectFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  // Each option's value is also what it shows.
  options: readonly string[];
  name?: string;
  hint?: string;
  error?: string;
}

// A native select in a Field: the phone's own picker, and the keyboard works as people expect.
export default function SelectField({
  id,
  label,
  value,
  onChange,
  options,
  name,
  hint,
  error,
}: SelectFieldProps) {
  return (
    <Field id={id} label={label} hint={hint} error={error}>
      {(control) => (
        <select
          {...control}
          name={name}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        >
          {options.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      )}
    </Field>
  );
}
