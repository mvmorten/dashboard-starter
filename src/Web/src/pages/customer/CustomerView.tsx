import {
  customerFieldLabels,
  fieldsOf,
  type Customer,
  type CustomerField,
} from "../../customers/customers";
import { BackLink, FieldValue, LastChanged } from "./parts";

// Without customers.edit: the same fields, to read.
export default function CustomerView({ customer }: { customer: Customer }) {
  const fields = fieldsOf(customer);
  return (
    <>
      <BackLink />
      <h1>{customer.name}</h1>
      <LastChanged customer={customer} />
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
    </>
  );
}
