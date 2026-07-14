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
| D6 | **Landed-cost apportionment (§26)** — the spec says "calculate landed cost" but never how. It decides the cost of every imported unit, and because costing is weighted average (D1) an error does not merely misprice the shipment: it feeds the moving average and **spreads to all existing stock of that product**. | **By value.** Each shipment line takes a share of the charges in proportion to its line value. Worked example: AED 3,000 of charges over 10 laptops at 1,000 (10,000) and 100 cables at 50 (5,000) → laptops take 2,000 (+200/unit, landing at 1,200), cables take 1,000 (+10/unit, landing at 60). Rounding differences go to the **largest line**, so the apportioned total is the charge total to the cent. Rejected: *by weight* (truest for freight, but the system stores no product weight, so it would be a schema change plus data entry on every item to buy accuracy nobody has asked for); *by quantity* (loads the same freight onto a cable as a laptop, which makes margin on cheap items nonsense); *per charge type* (most accurate, and revisitable — the apportionment basis is an enum on the charge, so a second basis is a new case rather than a rewrite). |
| D7 | **Tax model (§22, §31)** — the spec never says whether a price includes tax, and inclusive-vs-exclusive changes every price field and every line calculation in sales. | **Prices are tax-exclusive.** The stored price is what the customer is charged before tax; tax is computed on the **discounted net** and added. Discount comes off *before* tax — taxing the gross and then discounting would charge the customer tax on money they never paid. **No jurisdiction is hardcoded**: each company defines its own effective-dated `tax_rates` and a default, so a UAE shop configures 5% and a shop elsewhere configures its own; a company in a jurisdiction with no sales tax configures none and every line is legitimately zero. Zero-rated is a 0% rate. Every document **snapshots** the percentage onto its line, never referencing the rate row — which is why `POST /tax-rates/{id}/supersede` exists rather than an edit. **No e-invoicing integration** (FTA/ZATCA) is built; the rate model does not preclude one. |
| D8 | **Multi-currency sales (§26)** — purchases are in any currency, but can a customer be *invoiced* in one? | **No. Sales are raised in the company's base currency, and only in it.** `Company.BaseCurrency` is the company's choice at onboarding. Invoicing a customer in a foreign currency creates an FX exposure on the **receivable**, and the module that measures it does not exist; accepting the currency while quietly ignoring the exposure would be the worst of both. This is enforced in one place (`CompanyCurrency`), so the day the business decides otherwise, the compiler lists every document it protected. Note the asymmetry with purchasing, and that it is deliberate: the shop genuinely owes dollars to an overseas supplier (P4 books the FX gain on settlement), but it does not have to bill in them. |
| D9 | **When does a repair part leave stock (§28)?** At the moment it is fitted, or when the job is invoiced? Nothing in the spec says, and the difference is invisible until it is not. | **When it is consumed.** A screen fitted on Tuesday is physically gone on Tuesday, whether or not the customer ever pays and whether or not anybody raises an invoice. Deferring the movement to invoicing would leave the shop selling parts that are already inside somebody's laptop — and a **warranty job is never invoiced at all**, so its parts would be consumed by nobody and the system would believe them available for ever. The part goes out through `IStockLedger` like every other stock movement (a `RepairConsumption`), which is what makes it appear in the balance audit, the valuation and the movement report without any of them needing to know that repairs exist. Taking a part back out is a `RepairReturn` — a real movement, not an UPDATE undoing the first: a shop that could erase a consumption could erase the evidence that a part ever went missing. |
| D10 | **Is a warranty repair free (§30)?** The customer pays nothing — but does the *job* cost nothing? | **A warranty repair is costed, not free.** The customer's bill is zero; the cost is not. The parts still left the shelf and the outsourcing vendor still charged for the board, so both land on the job and its gross profit comes out **negative** — which is exactly what it should show. A warranty repair that booked no cost would make warranty look free, and the shop would never learn **which product line its warranty is quietly paying for**. That question — "which products keep coming back, and what is that costing us?" — is the entire reason §30 exists, and it is unanswerable if the cost is suppressed along with the charge. So `is_chargeable` suppresses the *charge* and never the *cost*. The corollary: **rejecting a warranty claim is what makes a job chargeable** — the parts booked as warranty work become billable and the shop stops eating them. |
| D11 | **What does a repair bill look like (§28)?** Its own document, or a sales invoice? | **An ordinary `SalesInvoice`.** There is no `repair_invoices` table. The alternative needs its own tax arithmetic, its own payment allocations, its own credit notes and its own place in the receivables ageing — and every one of those already exists, is proven, and is what P7 will report on. A repair bill is a bill: the customer does not care which department raised it, and neither does their balance. The invoice moves no stock (the parts left when they were fitted, D9) and carries the cost the ledger reported *then*, not a fresh one — by now the moving average will have moved, and the margin on the job would be quietly restated. A **labour line has no product**, so it takes the company's *default* tax rate, resolved server-side: a client that can choose the tax rate on a line is a client that can choose zero. |
| D5 | **Who creates a login, and what is it?** §3 implies a company registers itself; §2 says TechStorePro manages registered companies. Both cannot be true. | **Companies are onboarded by TechStorePro; there is no self-service signup.** A platform admin creates the company and its first user together; that owner then creates the rest of the company's staff. **A login is `username@COMPANYCODE`** — one field. The username is chosen by whoever creates the user and is unique **within a company**, so two shops may each have an "admin"; the company code disambiguates them and is unique platform-wide. Email is no longer a login: it is optional and non-unique, because a counter clerk may not have one. **A user belongs to exactly one company** — the multi-company login and its switcher are gone; someone working for two companies has two accounts. |

## 46. Open questions

These block the phase named against each. **Answer before that phase starts** — every one of them is
cheap to decide now and expensive to change later.

| # | Question | Blocks |
| - | -------- | ------ |
| ~~Q6~~ | ~~**Tax model.**~~ **Answered — D7.** Tax-exclusive; per-company effective-dated rates; no jurisdiction hardcoded; no e-invoicing in P5. | ✅ |
| ~~Q8~~ | ~~**Multi-currency sales.**~~ **Answered — D8.** Sales are in the company's base currency only. Foreign-currency invoicing is additive later; unlike the tax basis, adding it does not restate past invoices. | ✅ |
| Q7 | **SaaS billing (§4)** — which payment processor? ~~Is signup self-service with a card, or manual onboarding?~~ **The onboarding half is answered: manual, by a platform admin (D5).** The payment processor is still open. | SaaS platform |
| Q9 | **Deployment target and file storage.** Cloud or self-hosted? Where do §39 attachments and §28 repair photos live? Data-residency constraints? §43 wants automated backups — of what, to where, with what recovery objectives? | Foundation (storage), hardening (backup) |

Smaller, non-blocking confirmations are listed in [architecture.md §7.3](architecture.md) — barcode
symbology, whether the POS must keep selling **offline**, localisation/RTL, audit-log retention,
technician scheduling, and approval-chain depth.