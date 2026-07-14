import { api } from "@/lib/api-client";
import type { FinancialAccountKind } from "@/features/finance/types";

/**
 * The finance module's calls into the API.
 *
 * **The calls that move money send an `Idempotency-Key`.** Every one of them writes a row into the account
 * ledger, and a ledger is the one place where a double-click is not a cosmetic problem: a retried transfer
 * banks the till twice, and the second one takes money out of a drawer that no longer has it. The shop
 * finds out at closing, if it finds out at all.
 *
 * The key is generated **once per attempt**, not per render: a key that changed on re-render would be a
 * new request every time, which is exactly no protection at all.
 *
 * The plain PUTs — renaming an account, editing a category — carry no key. They are already idempotent:
 * sending the same name twice leaves the same name.
 */

function newIdempotencyKey() {
  return crypto.randomUUID();
}

interface Auth {
  token: string;
}

// --- Accounts -----------------------------------------------------------------------------------

/**
 * Open a till or a bank account (§33).
 *
 * The opening balance is **a movement, not a column** — it becomes the first row on the account's
 * statement. Which is why this call carries a key: it posts money.
 */
export function openAccount(
  { token }: Auth,
  body: {
    name: string;
    kind: FinancialAccountKind;
    branchId?: string | null;
    currencyCode?: string | null;
    bankName?: string | null;
    accountNumber?: string | null;
    allowsOverdraft?: boolean;
    openingBalance?: number;
    openedAt?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/finance/accounts", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/**
 * Rename an account, correct its bank details, grant or withdraw an overdraft, close it.
 *
 * Its currency and its kind are not editable and are not sent: both would restate every movement the
 * account has ever carried.
 */
export function updateAccount(
  { token }: Auth,
  id: string,
  body: {
    name: string;
    bankName?: string | null;
    accountNumber?: string | null;
    allowsOverdraft?: boolean;
    isActive?: boolean;
    notes?: string | null;
  },
) {
  return api.put<void>(`api/v1/finance/accounts/${id}`, {
    token,
    body: { ...body, id },
  });
}

/**
 * Bank the till; take a float out to the second shop. Two movements, never one.
 *
 * `amountIn` is what actually landed — the bank's figure, not a converted one. It may be omitted when both
 * accounts hold the same currency, and the server refuses to guess it when they do not.
 */
export function transferBetweenAccounts(
  { token }: Auth,
  body: {
    fromAccountId: string;
    toAccountId: string;
    amountOut: number;
    amountIn?: number | null;
    description?: string | null;
    reference?: string | null;
    occurredAt?: string | null;
  },
) {
  return api.post<void>("api/v1/finance/accounts/transfer", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

// --- Expenses -----------------------------------------------------------------------------------

/**
 * Record money the shop has spent that bought no stock (§34).
 *
 * Recorded and paid in one act: this writes the expense **and takes the money out of the account**. The
 * amount is in that account's currency — you cannot spend dollars out of a dirham account.
 */
export function recordExpense(
  { token }: Auth,
  body: {
    expenseCategoryId: string;
    branchId: string;
    financialAccountId: string;
    amount: number;
    description: string;
    supplierId?: string | null;
    expenseDate?: string | null;
    reference?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/finance/expenses", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Not a delete and not an edit: the money comes back as a movement of its own, and both rows stay. */
export function cancelExpense({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/finance/expenses/${id}/cancel`, {
    token,
    body: { id, reason },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

// --- Expense categories -------------------------------------------------------------------------

export function createExpenseCategory(
  { token }: Auth,
  body: { name: string; description?: string | null },
) {
  return api.post<string>("api/v1/finance/expense-categories", { token, body });
}

export function updateExpenseCategory(
  { token }: Auth,
  id: string,
  body: { name: string; description?: string | null; isActive?: boolean },
) {
  return api.put<void>(`api/v1/finance/expense-categories/${id}`, {
    token,
    body: { ...body, id },
  });
}
