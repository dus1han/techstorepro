/**
 * The finance reports (P7).
 *
 * These mirror the DTOs in `Application/Reports/Queries` field for field. A drifted field here does not
 * fail loudly — it renders `undefined` as a blank cell in a column of money, which on an ageing report is
 * the worst possible failure: a debt that silently reads as zero.
 */

export enum AgeingBucket {
  Current = 1,
  Days1To30 = 2,
  Days31To60 = 3,
  Days61To90 = 4,
  Days90Plus = 5,
}

export const BUCKET_LABELS: Record<AgeingBucket, string> = {
  [AgeingBucket.Current]: "Not due",
  [AgeingBucket.Days1To30]: "1–30 days",
  [AgeingBucket.Days31To60]: "31–60 days",
  [AgeingBucket.Days61To90]: "61–90 days",
  [AgeingBucket.Days90Plus]: "90+ days",
};

export interface ReceivablesInvoice {
  invoiceId: string;
  number: string;
  customerId: string;
  customerName: string;
  invoicedAt: string;
  dueAt: string;
  daysOverdue: number;
  bucket: AgeingBucket;
  total: number;
  paidAmount: number;
  creditedAmount: number;
  outstandingAmount: number;
  currencyCode: string;
}

export interface ReceivablesAgeingRow {
  customerId: string;
  customerName: string;
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  days90Plus: number;
  totalDue: number;
  credits: number;
  netReceivable: number;
  storedBalance: number;

  /** Must be zero. Null on a backdated report, where the stored balance is not comparable. */
  variance: number | null;
  storeCredit: number;
  openInvoices: number;
  oldestDueAt: string | null;
}

export interface ReceivablesTotals {
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  days90Plus: number;
  totalDue: number;
  credits: number;
  netReceivable: number;
  storedBalance: number;
  variance: number | null;
  storeCredit: number;
}

export interface ReceivablesAgeing {
  asOf: string;
  currencyCode: string;
  rows: ReceivablesAgeingRow[];
  totals: ReceivablesTotals;
  invoices: ReceivablesInvoice[];
}

export interface PayablesInvoice {
  invoiceId: string;
  number: string;
  supplierReference: string | null;
  supplierId: string;
  supplierName: string;
  invoicedAt: string;
  dueAt: string;
  daysOverdue: number;
  bucket: AgeingBucket;
  total: number;
  paidAmount: number;
  outstandingAmount: number;
  currencyCode: string;
  exchangeRate: number;
  outstandingBase: number;
}

export interface PayablesAgeingRow {
  supplierId: string;
  supplierName: string;
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  days90Plus: number;
  totalDue: number;
  advances: number;
  netPayable: number;
  storedBalance: number;
  variance: number | null;
  openInvoices: number;
  oldestDueAt: string | null;
}

export interface PayablesTotals {
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  days90Plus: number;
  totalDue: number;
  advances: number;
  netPayable: number;
  storedBalance: number;
  variance: number | null;
}

export interface PayablesAgeing {
  asOf: string;
  currencyCode: string;
  rows: PayablesAgeingRow[];
  totals: PayablesTotals;
  invoices: PayablesInvoice[];
}

export interface StatementLine {
  at: string;
  documentType: string;
  number: string;
  reference: string | null;
  debit: number;
  credit: number;
  runningBalance: number;
}

export interface CustomerStatement {
  customerId: string;
  customerName: string;
  currencyCode: string;
  from: string;
  to: string;
  openingBalance: number;
  lines: StatementLine[];
  closingBalance: number;
  storeCredit: number;
  storedBalance: number;
  variance: number | null;
}

export interface SupplierStatement {
  supplierId: string;
  supplierName: string;
  currencyCode: string;
  from: string;
  to: string;
  openingBalance: number;
  lines: StatementLine[];
  closingBalance: number;
  storedBalance: number;
  variance: number | null;
}
