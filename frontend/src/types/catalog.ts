/** Mirrors the API's catalog contracts (P2). Generated from OpenAPI in a later pass. */

export enum ProductKind {
  Product = 1,
  Service = 2,
  SparePart = 3,
}

export enum ProductCondition {
  BrandNew = 1,
  Refurbished = 2,
}

export enum TrackingMode {
  None = 1,
  Serial = 2,
  Batch = 3,
}

export enum CustomerType {
  WalkIn = 1,
  Individual = 2,
  Corporate = 3,
}

export enum SupplierType {
  Local = 1,
  Overseas = 2,
  RepairVendor = 3,
}

export enum DiscountMethod {
  Percentage = 1,
  FixedAmount = 2,
}

export interface Product {
  id: string;
  itemCode: string;
  sku: string;
  barcode: string | null;
  name: string;
  description: string | null;
  categoryId: string | null;
  categoryName: string | null;
  brandId: string | null;
  brandName: string | null;
  model: string | null;
  kind: ProductKind;
  condition: ProductCondition;
  trackingMode: TrackingMode;
  unit: string;
  purchasePrice: number;
  sellingPrice: number;
  marginPercent: number | null;
  taxRateId: string | null;
  taxRateName: string | null;
  warrantyMonths: number;
  reorderLevel: number;
  isActive: boolean;
}

export interface Customer {
  id: string;
  code: string;
  name: string;
  type: CustomerType;
  companyName: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  taxNumber: string | null;
  creditLimit: number;
  paymentTermDays: number;
  priceTierId: string | null;
  priceTierName: string | null;
  balance: number;
  isActive: boolean;
}

export interface Supplier {
  id: string;
  code: string;
  name: string;
  type: SupplierType;
  email: string | null;
  phone: string | null;
  address: string | null;
  country: string | null;
  taxNumber: string | null;
  defaultCurrency: string;
  paymentTermDays: number;
  leadTimeDays: number;
  balance: number;
  isActive: boolean;
}

export interface CategoryDto {
  id: string;
  name: string;
  parentId: string | null;
  parentName: string | null;
  isActive: boolean;
  productCount: number;
}

export interface BrandDto {
  id: string;
  name: string;
  isActive: boolean;
  productCount: number;
}

export interface TaxRateDto {
  id: string;
  name: string;
  percent: number;
  isDefault: boolean;
  validFrom: string;
  validTo: string | null;
  isActive: boolean;
  isInForce: boolean;
}

export interface PriceTierDto {
  id: string;
  name: string;
  isDefault: boolean;
  isActive: boolean;
  customerCount: number;
}

export interface DiscountDto {
  id: string;
  name: string;
  productId: string | null;
  productName: string | null;
  customerId: string | null;
  customerName: string | null;
  method: DiscountMethod;
  value: number;
  maxValue: number | null;
  validFrom: string;
  validTo: string | null;
  isActive: boolean;
  isInForce: boolean;
}

export interface PaymentMethodDto {
  id: string;
  name: string;
  kind: number;
  requiresReference: boolean;
  /**
   * Where money tendered this way lands (P7). A payment through a method with no account behind it is
   * refused — the alternative is money that arrived nowhere and is missed by nobody. It must be null for
   * store credit, which moves no money.
   */
  financialAccountId: string | null;
  financialAccountName: string | null;
  validFrom: string;
  validTo: string | null;
  isActive: boolean;
  isInForce: boolean;
}

export interface CurrencyDto {
  code: string;
  name: string;
  symbol: string | null;
  decimalPlaces: number;
}
