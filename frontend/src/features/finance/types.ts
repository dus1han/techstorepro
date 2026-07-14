/**
 * The finance module's API contracts (P7 slice 2).
 *
 * The numeric enums are the wire format (smallint), so they must match Domain/Finance exactly. A drifted
 * value here does not fail to compile — it labels an outbound expense as an opening balance on a statement
 * somebody is reconciling against their bank, which is the kind of bug that is only found by not being
 * found.
 */

export enum FinancialAccountKind {
  Cash = 1,
  Bank = 2,
}

export enum AccountTransactionSource {
  OpeningBalance = 1,
  CustomerPayment = 2,
  CustomerRefund = 3,
  SupplierPayment = 4,
  Expense = 5,
  ExpenseCancellation = 6,
  TransferOut = 7,
  TransferIn = 8,
}

export enum ExpenseStatus {
  Recorded = 1,
  Cancelled = 2,
}

export const ACCOUNT_KIND_LABELS: Record<FinancialAccountKind, string> = {
  [FinancialAccountKind.Cash]: "Cash",
  [FinancialAccountKind.Bank]: "Bank",
};

export const SOURCE_LABELS: Record<AccountTransactionSource, string> = {
  [AccountTransactionSource.OpeningBalance]: "Opening balance",
  [AccountTransactionSource.CustomerPayment]: "Customer payment",
  [AccountTransactionSource.CustomerRefund]: "Customer refund",
  [AccountTransactionSource.SupplierPayment]: "Supplier payment",
  [AccountTransactionSource.Expense]: "Expense",
  [AccountTransactionSource.ExpenseCancellation]: "Expense cancelled",
  [AccountTransactionSource.TransferOut]: "Transfer out",
  [AccountTransactionSource.TransferIn]: "Transfer in",
};

export interface FinancialAccount {
  id: string;
  name: string;
  kind: FinancialAccountKind;
  currencyCode: string;
  branchId: string | null;
  /** Null means company-wide — the bank account every branch pays into. */
  branchName: string | null;
  bankName: string | null;
  accountNumber: string | null;
  allowsOverdraft: boolean;
  isActive: boolean;
  notes: string | null;
  /** The SUM of the movements, in the account's own currency. There is no stored balance to drift. */
  balance: number;
  balanceBase: number;
}

export interface AccountStatementRow {
  occurredAt: string;
  source: AccountTransactionSource;
  sourceNumber: string | null;
  description: string;
  reference: string | null;
  in: number;
  out: number;
  runningBalance: number;
}

export interface AccountStatement {
  accountId: string;
  accountName: string;
  kind: FinancialAccountKind;
  currencyCode: string;
  from: string | null;
  to: string;
  openingBalance: number;
  rows: AccountStatementRow[];
  closingBalance: number;
}

export interface CashPositionLine {
  accountId: string;
  name: string;
  kind: FinancialAccountKind;
  currencyCode: string;
  branchName: string | null;
  balance: number;
  balanceBase: number;
}

export interface CashPosition {
  asOf: string;
  baseCurrency: string;
  accounts: CashPositionLine[];
  /** Totalled in base currency: a dirham till and a dollar account cannot be added up any other way. */
  cashTotalBase: number;
  bankTotalBase: number;
  totalBase: number;
}

export interface Expense {
  id: string;
  number: string;
  expenseCategoryId: string;
  categoryName: string;
  branchId: string;
  branchName: string;
  financialAccountId: string;
  accountName: string;
  supplierId: string | null;
  supplierName: string | null;
  description: string;
  /** In the currency of the account it was paid from — never the supplier's, never the base. */
  amount: number;
  currencyCode: string;
  exchangeRate: number;
  amountBase: number;
  expenseDate: string;
  reference: string | null;
  status: ExpenseStatus;
  cancelledReason: string | null;
  notes: string | null;
}

export interface ExpenseSummaryLine {
  categoryId: string;
  categoryName: string;
  count: number;
  totalBase: number;
}

export interface ExpenseSummary {
  from: string;
  to: string;
  categories: ExpenseSummaryLine[];
  /** Cancelled expenses are not in here — the money came back, so charging for it would be a double hit. */
  totalBase: number;
}

export interface ExpenseCategory {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  expenseCount: number;
}
