/**
 * Mirrors the API's inventory contracts (P3). Hand-written, like catalog.ts; being migrated to the
 * generated OpenAPI types — see `npm run codegen` and src/types/README.md.
 *
 * The numbers are the wire format (smallint), so they must match Domain/Inventory/*.cs exactly.
 */

export enum MovementType {
  OpeningBalance = 1,
  Receipt = 2,
  Sale = 3,
  SaleReturn = 4,
  PurchaseReturn = 5,
  TransferOut = 6,
  TransferIn = 7,
  AdjustmentIn = 8,
  AdjustmentOut = 9,
  RepairConsumption = 10,
  RepairReturn = 11,
  CountAdjustmentIn = 12,
  CountAdjustmentOut = 13,
}

export enum StockReferenceType {
  Opening = 1,
  GoodsReceipt = 2,
  Invoice = 3,
  Delivery = 4,
  CreditNote = 5,
  StockTransfer = 6,
  StockAdjustment = 7,
  StockCount = 8,
  RepairTicket = 9,
  PurchaseReturn = 10,
}

export enum AdjustmentReason {
  OpeningStock = 1,
  Damaged = 2,
  Lost = 3,
  Theft = 4,
  Expired = 5,
  InternalUse = 6,
  Sample = 7,
  DataCorrection = 8,
  Other = 9,
}

export enum TransferStatus {
  Draft = 1,
  InTransit = 2,
  Received = 3,
  Cancelled = 4,
}

export enum StockCountStatus {
  Counting = 1,
  PendingApproval = 2,
  Approved = 3,
  Cancelled = 4,
}

export enum ReservationStatus {
  Active = 1,
  Released = 2,
  Fulfilled = 3,
  Expired = 4,
}

export enum SerialStatus {
  InStock = 1,
  Reserved = 2,
  InTransit = 3,
  Sold = 4,
  InRepair = 5,
  Returned = 6,
  Scrapped = 7,
  ReturnedToSupplier = 8,
}

export enum SerialEventType {
  Received = 1,
  Reserved = 2,
  ReservationReleased = 3,
  Sold = 4,
  Returned = 5,
  TransferredOut = 6,
  TransferredIn = 7,
  SentToRepair = 8,
  ReturnedFromRepair = 9,
  Adjusted = 10,
  Scrapped = 11,
  Counted = 12,
}

/** What kind of lie the balance cache is telling — see GetBalanceAuditQuery. */
export enum DiscrepancyKind {
  Quantity = 1,
  Cost = 2,
  MissingBalance = 3,
}

// --- Stock on hand ------------------------------------------------------------------------------

export interface StockBalanceDto {
  productId: string;
  productName: string;
  sku: string;
  barcode: string | null;
  warehouseId: string;
  warehouseName: string;
  quantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  averageCost: number;
  totalValue: number;
  reorderLevel: number;
  isBelowReorderLevel: boolean;
}

// --- The stock card -----------------------------------------------------------------------------

export interface StockMovementDto {
  id: string;
  occurredAt: string;
  type: MovementType;
  productId: string;
  productName: string;
  sku: string;
  warehouseId: string;
  warehouseName: string;
  serialNumber: string | null;
  /** Signed: an out-movement is negative. */
  quantity: number;
  unitCost: number;
  value: number;
  balanceAfter: number;
  averageCostAfter: number;
  referenceType: StockReferenceType;
  referenceId: string | null;
  referenceNumber: string | null;
  notes: string | null;
}

// --- Historical stock ---------------------------------------------------------------------------

export interface HistoricalStockLineDto {
  productId: string;
  productName: string;
  sku: string;
  openingQuantity: number;
  purchases: number;
  sales: number;
  transfersIn: number;
  transfersOut: number;
  adjustments: number;
  repairs: number;
  closingQuantity: number;
  closingValue: number;
}

export interface HistoricalStockDto {
  from: string;
  to: string;
  warehouseId: string | null;
  lines: HistoricalStockLineDto[];
}

// --- Valuation ----------------------------------------------------------------------------------

export interface StockValuationLineDto {
  warehouseId: string;
  warehouseName: string;
  productCount: number;
  totalQuantity: number;
  totalValue: number;
}

export interface StockValuationDto {
  totalValue: number;
  byWarehouse: StockValuationLineDto[];
}

// --- Proving the cache --------------------------------------------------------------------------

export interface BalanceDiscrepancyDto {
  productId: string;
  productName: string;
  warehouseId: string;
  warehouseName: string;
  kind: DiscrepancyKind;
  cachedQuantity: number;
  ledgerQuantity: number;
  quantityDifference: number;
  cachedAverageCost: number;
  ledgerAverageCost: number;
  valueDifference: number;
}

export interface BalanceAuditDto {
  balancesChecked: number;
  agrees: boolean;
  discrepancies: BalanceDiscrepancyDto[];
}

// --- Adjustments --------------------------------------------------------------------------------

export interface AdjustmentLineDto {
  productId: string;
  productName: string;
  sku: string;
  serialNumber: string | null;
  quantity: number;
  unitCost: number;
  value: number;
  notes: string | null;
}

export interface AdjustmentDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseName: string;
  reason: AdjustmentReason;
  explanation: string;
  adjustedAt: string;
  netValue: number;
  stockCountId: string | null;
  lines: AdjustmentLineDto[];
}

// --- Transfers ----------------------------------------------------------------------------------

export interface TransferLineDto {
  id: string;
  productId: string;
  productName: string;
  sku: string;
  serialNumber: string | null;
  quantity: number;
  receivedQuantity: number;
  shortfallQuantity: number;
  unitCost: number;
}

export interface TransferDto {
  id: string;
  number: string;
  fromWarehouseId: string;
  fromWarehouseName: string;
  toWarehouseId: string;
  toWarehouseName: string;
  status: TransferStatus;
  shippedAt: string | null;
  receivedAt: string | null;
  hasShortfall: boolean;
  notes: string | null;
  lines: TransferLineDto[];
}

// --- Stock counts -------------------------------------------------------------------------------

export interface CountLineDto {
  id: string;
  productId: string;
  productName: string;
  sku: string;
  serialNumber: string | null;
  systemQuantity: number;
  countedQuantity: number;
  variance: number;
  unitCost: number;
  varianceValue: number;
  notes: string | null;
}

export interface CountDto {
  id: string;
  number: string;
  warehouseId: string;
  warehouseName: string;
  status: StockCountStatus;
  startedAt: string;
  countedAt: string | null;
  approvedAt: string | null;
  stockAdjustmentId: string | null;
  netVarianceValue: number;
  varianceLineCount: number;
  notes: string | null;
  lines: CountLineDto[];
}

// --- Reservations -------------------------------------------------------------------------------

export interface ReservationDto {
  id: string;
  productId: string;
  productName: string;
  sku: string;
  warehouseId: string;
  warehouseName: string;
  serialNumber: string | null;
  quantity: number;
  fulfilledQuantity: number;
  outstandingQuantity: number;
  status: ReservationStatus;
  referenceType: StockReferenceType;
  referenceId: string | null;
  referenceNumber: string | null;
  reservedAt: string;
  expiresAt: string | null;
  releasedAt: string | null;
  notes: string | null;
}

// --- Serials ------------------------------------------------------------------------------------

export interface SerialDto {
  id: string;
  serialNumber: string;
  productId: string;
  productName: string;
  sku: string;
  status: SerialStatus;
  warehouseId: string | null;
  warehouseName: string | null;
  purchaseCost: number;
  supplierId: string | null;
  supplierName: string | null;
  warrantyUntil: string | null;
  isUnderWarranty: boolean;
  soldInvoiceLineId: string | null;
}

export interface SerialEventDto {
  type: SerialEventType;
  status: SerialStatus;
  warehouseId: string | null;
  warehouseName: string | null;
  referenceType: StockReferenceType | null;
  referenceId: string | null;
  referenceNumber: string | null;
  notes: string | null;
  at: string;
}

export interface SerialHistoryDto {
  serial: SerialDto;
  events: SerialEventDto[];
}
