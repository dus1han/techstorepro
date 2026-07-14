/**
 * The repairs module's API contracts (P6).
 *
 * The numeric enums are the wire format (smallint), so they must match Domain/Repairs exactly. A drifted
 * value here does not fail to compile — it silently renders "Received" for a delivered job, which is the
 * kind of bug that survives a demo.
 */

export enum RepairTicketStatus {
  Received = 1,
  Diagnosing = 2,
  AwaitingApproval = 3,
  InRepair = 4,
  Testing = 5,
  Ready = 6,
  Delivered = 7,
  Cancelled = 8,
}

export const REPAIR_STATUS_LABELS: Record<RepairTicketStatus, string> = {
  [RepairTicketStatus.Received]: "Received",
  [RepairTicketStatus.Diagnosing]: "Diagnosing",
  [RepairTicketStatus.AwaitingApproval]: "Awaiting approval",
  [RepairTicketStatus.InRepair]: "In repair",
  [RepairTicketStatus.Testing]: "Testing",
  [RepairTicketStatus.Ready]: "Ready for collection",
  [RepairTicketStatus.Delivered]: "Delivered",
  [RepairTicketStatus.Cancelled]: "Cancelled",
};

/** Who is standing behind the repair, and therefore who pays for it (§30). */
export enum RepairWarrantyType {
  None = 0,
  Shop = 1,
  Manufacturer = 2,
  Supplier = 3,
}

export const WARRANTY_TYPE_LABELS: Record<RepairWarrantyType, string> = {
  [RepairWarrantyType.None]: "Chargeable",
  [RepairWarrantyType.Shop]: "Shop warranty",
  [RepairWarrantyType.Manufacturer]: "Manufacturer warranty",
  [RepairWarrantyType.Supplier]: "Supplier warranty",
};

export enum OutsourcingStatus {
  Sent = 1,
  Returned = 2,
  Cancelled = 3,
}

export const OUTSOURCING_STATUS_LABELS: Record<OutsourcingStatus, string> = {
  [OutsourcingStatus.Sent]: "At vendor",
  [OutsourcingStatus.Returned]: "Back from vendor",
  [OutsourcingStatus.Cancelled]: "Recalled",
};

export enum WarrantySourceType {
  SalesInvoiceLine = 1,
  GoodsReceiptLine = 2,
  Serial = 3,
}

export enum WarrantyClaimStatus {
  Open = 1,
  Accepted = 2,
  Rejected = 3,
}

export const CLAIM_STATUS_LABELS: Record<WarrantyClaimStatus, string> = {
  [WarrantyClaimStatus.Open]: "Open",
  [WarrantyClaimStatus.Accepted]: "Accepted",
  [WarrantyClaimStatus.Rejected]: "Rejected",
};

export interface RepairPart {
  id: string;
  productId: string;
  productName: string;
  warehouseId: string;
  quantity: number;
  /** COGS — what the ledger valued it at when it left the shelf. Not what it costs today. */
  unitCost: number;
  unitPrice: number;
  isChargeable: boolean;
  isReturned: boolean;
  costTotal: number;
  chargeTotal: number;
  consumedAt: string;
  notes: string | null;
}

export interface RepairLabour {
  id: string;
  technicianId: string | null;
  description: string;
  hours: number;
  hourlyRate: number;
  isChargeable: boolean;
  chargeTotal: number;
  workedAt: string;
}

export interface RepairDiagnosis {
  id: string;
  technicianId: string | null;
  findings: string;
  recommendedAction: string | null;
  estimatedCost: number | null;
  diagnosedAt: string;
}

export interface RepairOutsourcing {
  id: string;
  vendorSupplierId: string;
  vendorName: string;
  status: OutsourcingStatus;
  sentAt: string;
  expectedAt: string | null;
  receivedAt: string | null;
  cost: number;
  currencyCode: string;
  exchangeRate: number;
  costInBaseCurrency: number;
  notes: string | null;
}

export interface RepairStatusChange {
  fromStatus: RepairTicketStatus;
  toStatus: RepairTicketStatus;
  changedBy: string | null;
  changedAt: string;
  notes: string | null;
}

export interface RepairTicket {
  id: string;
  number: string;
  customerId: string;
  customerName: string;
  branchId: string;
  deviceProductId: string | null;
  deviceProductName: string | null;
  deviceSerialNumber: string | null;
  reportedFault: string;
  accessories: string | null;
  conditionNotes: string | null;
  status: RepairTicketStatus;
  warrantyType: RepairWarrantyType;
  isWarranty: boolean;
  warrantyInvoiceLineId: string | null;
  estimatedCost: number | null;
  approvedAt: string | null;
  technicianId: string | null;
  receivedAt: string;
  promisedAt: string | null;
  deliveredAt: string | null;

  /**
   * The money (§35, "repair profitability").
   *
   * `grossProfit` is **negative on a warranty job, and that is correct**: the parts still left the shelf
   * and the vendor still charged for the board; only the customer's bill is zero. A warranty repair that
   * showed no cost would make warranty look free.
   */
  partsCost: number;
  outsourcingCost: number;
  totalCost: number;
  chargeableTotal: number;
  grossProfit: number;

  /** The invoice raised for this job, if it has been billed. A warranty job never has one. */
  salesInvoiceId: string | null;

  notes: string | null;
  cancelledReason: string | null;
  parts: RepairPart[];
  labour: RepairLabour[];
  diagnoses: RepairDiagnosis[];
  outsourcings: RepairOutsourcing[];
  statusHistory: RepairStatusChange[];
}

/** What the shop knows about the machine on the counter. */
export interface WarrantyCover {
  serialId: string | null;
  productId: string | null;
  soldInvoiceLineId: string | null;
  warrantyType: RepairWarrantyType;
  warrantyId: string | null;
  coveredUntil: string | null;
  /** Why the answer is what it is, in words a person can repeat to a customer. */
  explanation: string;
  isCovered: boolean;
}

export interface Warranty {
  id: string;
  warrantyType: RepairWarrantyType;
  sourceType: WarrantySourceType;
  productId: string;
  productName: string;
  serialNumber: string | null;
  startsOn: string;
  endsOn: string;
  terms: string | null;
  openClaims: number;
}

export interface WarrantyClaim {
  id: string;
  warrantyId: string;
  warrantyType: RepairWarrantyType;
  productName: string;
  serialNumber: string | null;
  repairTicketId: string | null;
  repairTicketNumber: string | null;
  status: WarrantyClaimStatus;
  claimedAt: string;
  resolvedAt: string | null;
  outcome: string | null;
  /** What honouring this claim cost the shop — the parts and the vendor off the job it authorised. */
  costToShop: number;
}
