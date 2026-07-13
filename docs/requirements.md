# TechStorePro ERP Requirements Specification

## Product Name
TechStorePro

## Product Type
Multi-company SaaS ERP platform for computer selling, importing, inventory management, and repair businesses.

## Technology Stack

Frontend:
- Next.js
- TypeScript
- Tailwind CSS

Backend:
- ASP.NET Core Web API

Database:
- PostgreSQL

## Core Principles

The system must be:

- Multi-company SaaS
- Multi-branch
- Multi-warehouse
- Configurable without code changes
- Feature permission based
- Secure
- Audit compliant
- Scalable

## General Rules

1. All transactions must maintain:
   - Company
   - Branch
   - Warehouse (where applicable)
   - User
   - Created date/time
   - Modified date/time

2. All configurable settings must support:
   - Valid From date
   - Valid To date
   - Active/Inactive status

3. Historical transactions must not change when settings are updated.

4. Business rules should not be hardcoded.
   They must be configurable from Settings.

5. All important actions must be recorded in Audit Trail.

TechStorePro
Full Feature Specification
Multi-Company SaaS ERP for Computer Sales, Import, Inventory & Repair Businesses
1. Platform Overview

TechStorePro is a multi-company SaaS ERP platform designed for:

Computer retail shops
Computer wholesalers
Computer importers
Refurbished computer sellers
IT equipment suppliers
Computer repair businesses

The system must support:

Multiple companies
Multiple branches
Multiple warehouses
Multiple users
Different business workflows
Configurable settings
Feature-based permissions
SaaS subscription management
2. SaaS Platform Management
TechStorePro Admin Panel

For TechStorePro platform owners.

Manage:

Registered companies
Subscription plans
Billing
Payments
Feature availability
System usage
Storage usage
User count
System logs
3. Company Registration & Tenant Management
Company Registration

A new company can register with:

Company name
Email address
Contact person
Phone number
Address
Country
VAT/Tax information

System creates:

Company profile
Initial admin user
Default settings
Subscription record
4. SaaS Subscription Management
Subscription Plans

Configure:

Plan name
Monthly price
Annual price
Trial period
Maximum users
Number of branches
Storage limit
Available features
Subscription Lifecycle

Support:

Trial
Active
Expired
Suspended
Cancelled
Renewed
SaaS Billing

Automatically generate monthly bills.

Include:

Subscription invoice number
Billing period
Plan details
Amount
Tax/VAT
Due date
Payment instructions
SaaS Billing Email

Send monthly invoices to the registered company email address.

Notifications:

New subscription
Monthly invoice
Payment reminder
Expiry warning
Suspension notice
5. Company Management
Company Profile

Configure:

Company name
Logo
Address
Contact details
Email
Website
Registration number
VAT number
Bank details
Currency
Time zone
Branch Management

Support:

Multiple shops
Repair centers
Warehouses

Branch details:

Branch name
Address
Contact details
Default warehouse
Document numbering
6. User Management & Security
User Management

Create users with:

Name
Email
Phone
Status
Branch access
Company access
7. Feature-Based Permission System

No fixed roles.

Company Admin can assign permissions by feature.

Permissions:

View
Create
Edit
Delete
Approve
Print
Export

Apply to:

Modules
Screens
Actions
APIs
8. Security Management
Authentication

Support:

Secure login
Password encryption
JWT authentication
Refresh tokens
Logout
Session management
Security Controls

Support:

Password policies
Failed login protection
Account lock
Session timeout
Login history
Device tracking
IP tracking
Data Isolation

Each company must only access its own data.

Company users cannot access:

Other company customers
Other company stock
Other company transactions
9. Audit Trail & Activity Timeline
Audit Log

Track:

Create
Edit
Delete
Approve
Cancel
Print
Export
Login
Permission changes
Settings changes

Store:

User
Date/time
IP
Old value
New value
Activity Timeline

Available for:

Customers
Suppliers
Products
Sales
Purchases
Repairs
Payments
Credit notes
10. Soft Delete Management

Important records should not permanently delete.

Support:

Delete flag
Deleted user
Deleted date
Delete reason
Restore option
11. System Settings

All business rules must be configurable.

Effective Date Configuration

All settings support:

Valid From
Valid To
Active/Inactive

Applicable:

Tax rates
Prices
Discounts
Payment methods
Warranty periods
Numbering rules

Historical transactions retain old values.

12. Document Management

Generate:

Quotations
Sales invoices
Receipts
Purchase documents
GRN
Credit notes
Debit notes
Repair documents
Reports
Document Configuration

Configure:

Logo
Company details
VAT details
Bank details
Terms and conditions
Footer
Signatures
13. Email & Notification Management (Optional)

Company Admin can enable/disable.

Settings:

Enable email notifications
Enable automatic document sending
Enable repair notifications
Enable payment notifications
Company Email Configuration

Optional:

SMTP settings
Sender email
Sender name
Reply email
Email signature
Automated Emails

Send:

Sales
Quotations
Invoices
Payment receipts
Credit notes
Repair
Job received
Diagnosis completed
Approval request
Job completed
Ready for collection
Finance
Payment reminders
Statements
Email Templates

Configure:

Subject
Body
Attachments

Variables:

Customer name
Invoice number
Amount
Repair number
Company name
Notification History

Track:

Recipient
Date/time
Status
Error details
14. Customer Management (CRM)
Customer Types
Walk-in
Individual
Corporate
Customer Details

Maintain:

Name
Company
Contact details
Email
Address
VAT number
Credit limit
Payment terms
Customer History

View:

Quotations
Invoices
Payments
Repairs
Warranty
Returns
Credit notes
15. Supplier Management

Support:

Local suppliers
Overseas suppliers
Repair vendors

Maintain:

Supplier details
Currency
Payment terms
VAT details
Purchase history
16. Product / Item Master

Manage:

Products
Services
Spare parts
Product Fields
Item code
SKU
Barcode
Name
Category
Brand
Model
Specifications
Purchase price
Selling price
Warranty
Tax category
Product Type

Selectable:

Brand New
Refurbished
17. Barcode Management

Support:

Barcode generation
Barcode printing
QR codes

Barcode printing from:

Item master
GRN

Support:

Thermal printers
Sticker sheets
Batch printing
18. Serial Number Management

Track:

Laptop serial numbers
Desktop serial numbers
Monitor serial numbers
Printer serial numbers

History:

Purchase
Supplier
Customer
Warranty
Repair
19. Inventory Management

Support:

Multiple warehouses
Multiple branches
Stock movement
Stock adjustments
Stock transfers
Historical Stock

View stock on previous dates.

Show:

Opening stock
Purchases
Sales
Transfers
Adjustments
Repairs
Closing stock
20. Stock Reservation

Support:

Reserve stock from quotations
Prevent overselling
Release reservation
21. Physical Stock Count

Features:

Stock counting
Barcode scanning
Difference calculation
Adjustment approval
22. Sales Management
POS Sales

Features:

Barcode scanning
Product search
Customer selection
Discount
Multiple payments
Quotations

Support:

Create quotation
Convert to invoice
Email quotation
Print PDF
Sales Invoice

Support:

Invoice generation
Partial payment
Multiple payment methods
Outstanding tracking
23. Payment Management

All payment areas must capture:

Payment method
Payment reference
Date
Amount
Currency
Remarks
Payment Methods

Configurable:

Cash
Bank transfer
Card
Cheque
Online payment
Custom methods

Support:

Full payment
Partial payment
Multiple payment methods
24. Returns & Credit Notes
Customer Returns

Options:

Exchange
Store credit
Credit note
Cash refund
Bank refund
Credit Notes

Support:

Invoice reference
Returned items
Tax
Customer balance
Future usage
25. Purchase Management

PR is not required.

PO is optional.

Local Purchase

Flow:

Supplier

↓

Quotation (Optional)

↓

PO (Optional)

↓

GRN

↓

Invoice

↓

Payment

Direct Purchase

Supplier

↓

GRN

↓

Stock Update

↓

Invoice

26. Import Purchase Management

Support:

Overseas suppliers
Foreign currency
Import orders
Shipment tracking

Track:

Freight
Insurance
Customs
Clearing charges

Calculate:

Landed cost
27. Goods Receipt Note (GRN)

GRN must:

Add stock
Capture serial numbers
Generate barcodes
Print stickers
28. Repair Management
Repair Job Sheet

Capture:

Customer
Device
Serial number
Complaint
Accessories
Images
Diagnosis
Estimated cost
Repair Workflow

Received

↓

Diagnosis

↓

Customer Approval

↓

Repair

↓

Testing

↓

Ready

↓

Delivered

29. Outsourced Repair

Track:

Vendor
Sent date
Received date
Cost
Status
30. Warranty Management

Support:

Manufacturer warranty
Supplier warranty
Shop warranty

Track:

Warranty expiry
Claims
Repairs
31. Price Management

Support:

Purchase price
Selling price
Wholesale price
Corporate price

Maintain:

Price history
Effective dates
32. Discount Management

Support:

Product discounts
Customer discounts
Percentage discounts
Fixed discounts

Approval:

Discount limits
Manager approval
33. Finance Management

Track:

Customer receivables
Supplier payables
Cash
Bank
Expenses
34. Expense Management

Manage:

Rent
Transport
Import costs
Repair costs
Other expenses
35. Reports
Sales Reports
Sales summary
Product sales
Customer sales
Profit
Inventory Reports
Current stock
Historical stock
Stock valuation
Movement
Purchase Reports
Supplier purchases
Import costs
Repair Reports
Pending repairs
Repair profitability
Finance Reports
Outstanding balances
Profit & loss
36. Dashboard

Animated dashboard widgets:

Sales
Profit
Stock value
Low stock
Pending repairs
Payments due
37. Global Search

Search:

Product
Barcode
Serial number
Customer
Supplier
Invoice
Repair job
38. Internal Notes

Private notes for:

Customers
Suppliers
Products
Repairs
Documents

Track:

User
Date
History
39. Attachments

Upload:

Images
Documents
Warranty cards
Import documents
Repair photos
40. Keyboard Shortcuts

Support:

Search
Save
New invoice
New quotation
Barcode scanning
Navigation
41. Theme & User Preferences

Support:

Light mode
Dark mode
Dashboard customization
Favourite modules
Default landing page
42. Import / Export

Excel support:

Import:

Products
Customers
Suppliers
Opening stock

Export:

Reports
Inventory
Transactions
43. Backup & Recovery

Support:

Automated backups
Restore points
Recovery process
44. API & Integration Ready

Future integrations:

E-commerce
Payment gateways
WhatsApp
SMS
Courier systems
Accounting systems

---

## 45. Decisions taken

These resolve ambiguities in the specification above. The full reasoning is in
[architecture.md](architecture.md); the summary is here because the spec is what the business reads.

| # | Question | Decision |
| - | -------- | -------- |
| D1 | **Inventory costing method** — the spec never states one, but it determines COGS on every sale and the value of every stock report. | **Weighted average.** Moving average per (product, warehouse), raised by *landed* cost. FIFO is not built. |
| D2 | **Warehouse model** — §5 gives a branch a "default warehouse"; the core principles demand multi-warehouse. | **Both, configurable.** A warehouse may be owned by a branch, or shared at company level with an explicit list of branches allowed to use it. Stock is keyed by warehouse. |
| D3 | **General ledger** — §33 asks for AR/AP/cash/bank/expenses only, but §35 asks for a Profit & Loss report. | **No general ledger.** P&L is computed (revenue − COGS − expenses) and transactions export to an external accounting package (§44). It will **not** reconcile line-for-line to an accountant's books; this is an accepted trade. |
| D4 | **"No fixed roles" (§7)** — how literally? | **Strictly per user.** Permission is a (feature, action) pair granted to a user. No roles, no stored templates, nothing resolving a role name. Bulk assignment is a UI convenience that writes individual grants. |

## 46. Open questions

These block the phase named against each. **Answer before that phase starts** — every one of them is
cheap to decide now and expensive to change later.

| # | Question | Blocks |
| - | -------- | ------ |
| Q2 | **Landed cost (§26) — apportion by value, by weight, or by quantity?** The spec says "calculate landed cost" but never how. Worked examples from the business are needed, to be turned into tests before the code. Because costing is weighted average (D1), an error here spreads to *all* stock of a product, not just the imported units. | Purchasing & imports |
| Q6 | **Tax model.** One VAT rate or many? Prices tax-**inclusive** or tax-**exclusive**? Which jurisdiction? Is a tax e-invoicing integration (FTA / ZATCA / equivalent) required? Inclusive-vs-exclusive changes every price field and every line calculation. | Sales |
| Q8 | **Multi-currency sales.** §26 covers foreign-currency *purchases*. Can a customer be *invoiced* in a foreign currency, or is selling always in the company's base currency? FX gain/loss on receivables is a sub-module in its own right. | Sales |
| Q7 | **SaaS billing (§4)** — which payment processor? Is signup self-service with a card, or manual onboarding? | SaaS platform |
| Q9 | **Deployment target and file storage.** Cloud or self-hosted? Where do §39 attachments and §28 repair photos live? Data-residency constraints? §43 wants automated backups — of what, to where, with what recovery objectives? | Foundation (storage), hardening (backup) |

Smaller, non-blocking confirmations are listed in [architecture.md §7.3](architecture.md) — barcode
symbology, whether the POS must keep selling **offline**, localisation/RTL, audit-log retention,
technician scheduling, and approval-chain depth.