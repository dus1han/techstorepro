# Development plan

> Status: **P0–P6 complete.** The shop can buy, sell and repair, through screens rather than curl:
> purchase order → goods receipt → landed cost → supplier invoice → payment; quote → order → delivery →
> invoice → payment, plus a POS till, returns and store credit; and intake → diagnosis → customer approval
> → parts → labour → outsourcing → collection → bill. All three paths are proven end to end against a live
> API, not only by tests.
>
> **P6 closed the loop the whole design was pointing at.** A laptop sold in P5 comes back to the workshop
> two years later and the system knows, without being told, that the repair is free — by walking the serial
> back to the invoice line that sold it. That is why the delivery binds the serial, and it is why Sales had
> to ship before Repairs.
>
> **P4 had been marked ✅ while half of it did not exist** — the entities and tables for purchase orders,
> supplier invoices and supplier payments were real, but nothing above them was, so the FX gain the phase
> describes had nowhere to be computed. That is closed; see P4 below for what was missing and why nobody
> noticed.
>
> **P7 is under way.** Its first slice — receivables, payables and statements — added no tables: the
> documents that answer "who owes what" have existed since P4 and P5, and what was missing was only the
> arithmetic that reads them. It reports a **variance** on every row, because the balances it reports on are
> hand-maintained caches that nothing had ever checked; it comes out at zero, and the tests fail if it does
> not. Proving it also turned up a real modelling gap in P5 — **a credit note writes no allocation, so a
> fully-credited invoice shows as unpaid for ever** — which the reports work around and the debt register
> now names.
>
> Phases are numbered P0–P9 and match [architecture.md §6](architecture.md). The earlier M0–M8
> milestone numbering is gone: it mapped almost one-to-one, and keeping two schemes alive only
> invited someone to cite the wrong one. Where this file and architecture.md disagree, architecture.md
> wins.

## Principles

1. **Tenancy is not a feature you add later.** It went into the foundation because retrofitting a
   `company_id` onto a live schema is a migration nightmare and a leak waiting to happen.
2. **Build the spine before the limbs.** Inventory is what sales, purchasing and repairs all touch.
   It comes before any of them, or the stock logic gets written three times and reconciled forever.
3. **Every phase is shippable.** A phase ends with something the shop could actually use, not a
   half-wired layer.
4. **Answer the open question before you build the phase it blocks** (see
   [requirements.md §46](requirements.md)). Costing is decided (weighted average, D1) and so is
   landed cost (by value, D6 — its worked example became a test before a line of the code existed).
   **The tax model (Q6) is the expensive one still open**: inclusive-vs-exclusive changes every price
   field and every line calculation in sales.
5. **The cross-tenant denial test is a gate, not a nicety.** It runs at every phase boundary. One
   database holds every company's data; that test is the only thing standing between them.

---

## Phases

### P0 — Foundation ✅ done

Solution structure, Clean Architecture layering, tenancy plumbing, error contract, health check,
Docker Postgres, docs. No business modules.

**Exit criteria (met):** build clean; API boots; `/health` returns 200; frontend builds.

### P1 — Identity, tenancy, permissions, settings, audit ✅ done

Nothing else could be built first: every other module needs a `company_id` on the token and a user
to attribute changes to.

**Delivered:**

- **Entities (16 tables):** `Company`, `Branch`, `Warehouse`, `BranchWarehouse`, `User`,
  `CompanyUser`, `CompanyUserBranch`, `UserPermission`, `RefreshToken`, `LoginHistory`, `Feature`,
  `SettingDefinition`, `SettingValue`, `DocumentNumberSequence`, `AuditLog`.
  **There is no `Role` entity and no `role` claim** — requirements §45 D4.
- **Permissions (§7):** per-user `(feature, action)` grants across 8 features × 7 actions. Resolved
  per request from the database, never from the token, so a revoked permission takes effect on the
  caller's *next* request rather than whenever their token happens to expire. The owner holds
  everything implicitly, which is what stops a company revoking its own last administrator and
  bricking itself.
- **Auth (§8):** register, login, refresh with rotation and **replay detection** (a replayed token
  kills the whole chain), logout, switch-company, `/auth/me`. PBKDF2-SHA256 at 210k iterations;
  failed-login lockout; login history with IP and device.
- **Warehouses (D2):** branch-owned (`branch_id` set) or company-shared (`branch_id` null) with an
  explicit per-branch access list. "Shared" never silently means "any branch may drain it".
- **Settings (§11):** effective-dated and versioned, never overwritten, so a document raised in
  March still resolves March's value.
- **Document numbering (§5):** gapless per (company, branch, type, year), `SELECT … FOR UPDATE`
  inside the caller's transaction so a rollback returns the number instead of burning it.
- **Audit trail (§9):** written from the EF change tracker — old and new values, user, IP — so no
  handler can forget it. The password hash is never recorded.
- **Soft delete (§10):** mandatory delete reason, plus restore.
- **Frontend:** login, route guard, app shell, company switcher, permission-filtered navigation, and
  the per-user permission grid with bulk row/column toggles (a UI affordance that writes individual
  grants — nothing is stored as a reusable bundle).

**Tests: 22 green.** 16 domain unit tests, plus **6 cross-tenant isolation tests against a real
PostgreSQL** (Testcontainers): company A cannot read company B's row even holding its exact id,
cannot update it, and a row A creates never surfaces in B. That suite is the gate on P2 and it passes.

### P2 — Master data ✅ done

**Delivered (14 new tables, 30 in total):**

- **Products** — serial / batch / no tracking; product, service or spare part; brand-new or
  refurbished; barcode, SKU and item code; margin computed, never stored.
- **Customers and suppliers** — walk-in / individual / corporate; local / overseas / repair vendor.
- **Pricing that actually prices.** Price tiers, price lists, and an `IPriceResolver` that resolves
  customer tier → default tier → product price, and **reports which one it used**. Verified live: a
  wholesale customer pays 3,600 where a walk-in pays 3,950, and the system can say why.
- **Effective-dated** tax rates, discounts and payment methods; **price history** per product.
- **Currencies and FX** — a starter ISO set seeded, one rate per currency per day.

**Domain rules enforced on the entity, not in a validator** — so no code path can dodge them:

| Rule | Why |
| --- | --- |
| A service cannot be serial-tracked | A stock ledger entry for "one hour of labour" is meaningless |
| A walk-in cannot be given a credit limit | There is no account to bill |
| Two price lists for one tier cannot overlap | "What does this customer pay today?" would have two answers |
| A price floor cannot exceed the price | Every sale would need approval from day one |
| A fixed discount cannot exceed the line | The customer would be owed money for buying something |
| A discount's approval ceiling cannot sit below the discount | Every use of the rule would stall in approval |
| A customer or supplier with a balance cannot be retired | It would hide the debt from the receivables report |

**Frontend primitives landed** — `<DataTable>` (server paging, debounced search, sort) and
`<EntityForm>` (binds the API's RFC 7807 `errors` map onto the right inputs, so a validation rule is
written **once**, on the server). Forty later screens reuse both.

**TanStack Query adopted** (architecture.md §5.3 always called for it). Manual `useEffect` + `useState`
fetching was re-fetching on every dependency change, racing its own responses, caching nothing — and
Next 16's React Compiler lint rejects it outright. Query fixed the whole class and deleted code. The
**cache key includes the active company**, so switching company cannot serve tenant A's rows under
tenant B's name — a server-side isolation guarantee that a client cache could otherwise undo.

**Tests: 47 green** (39 domain + 8 cross-tenant). The isolation suite grew with the schema: company B
cannot see A's products, customers or suppliers, gets a 404 fetching A's product by its real id, and
**can reuse the same SKU** — uniqueness is scoped per company, not global. A retired product frees
its SKU, so a mistyped code is not held hostage forever by a soft-deleted row.

### P3 — Inventory (the spine) ✅ done

**Delivered (12 new tables, 42 in total):**

- **The stock ledger.** `IStockLedger` is the single door: nothing else in the system may write
  `stock_movements`, `stock_balances` or a serial's status. Every method **requires an ambient
  transaction and throws without one** — a stock movement is never the only thing a business
  operation does, and a ledger that quietly committed on its own would leave stock moved and the
  document that moved it rolled back.
- **The lock is the design.** The balance row is materialised and locked
  (`INSERT … ON CONFLICT DO NOTHING`, then `SELECT … FOR UPDATE`) before it is read, and the weighted
  average is recomputed *inside* that lock. The upsert is not redundant: `FOR UPDATE` locks nothing
  when the row does not exist yet, and two concurrent first-receipts would both insert.
- **Serial lifecycle** — a state machine, not a status column. `Scrapped` and `ReturnedToSupplier` are
  terminal; a sold unit never silently returns to the shelf. This is what stops the same laptop being
  sold twice, which quantities alone cannot.
- **Reservations** (§20), **transfers** (two movements, so in-transit stock belongs to neither end),
  **adjustments** (post immediately; mandatory reason), **stock counts** (§21: system quantity
  snapshotted at scan time, approval a separate permission because it authorises a write-off),
  **historical stock** (§19), **valuation**, and **barcode/QR label printing** to PDF (§17).
- **`available = quantity − reserved_quantity`.** That subtraction is what "prevent overselling" means,
  and it is enforced under the lock.

**The cache proves itself.** `stock_balances` is a cache of `stock_movements`, so `/inventory/balance-audit`
recomputes it from the ledger — and `InventoryMaintenanceService` runs **the same audit** nightly, per
company, alongside a 15-minute sweep of expired reservations (a forgotten quote would otherwise hold
the last unit off the shelf forever). The audit compares **cost as well as quantity**: a balance whose
units are right and whose average is wrong looks healthy while every sale from that warehouse books
the wrong COGS. It reports and never repairs — a disagreement means something wrote stock outside
`IStockLedger`, and overwriting the evidence would destroy the only trace of it.

**Tests: 142 green** (110 domain + 32 integration), up from 47. The ledger's guarantees are all claims
about what the *database* does — `FOR UPDATE`, `ON CONFLICT`, real transactions — so they are tested
against a real PostgreSQL and not an in-memory provider that would pass while the real thing oversold
the last laptop. The suite proves overselling is refused, that a reservation cannot be double-promised,
that the same serial cannot be sold twice, that a rolled-back transaction leaves no stock, **and that
the audit itself fails when a balance is corrupted behind the ledger's back** — in quantity *or* in
cost. The cross-tenant gate grew to cover all ten new tenant-scoped tables.

### P4 — Purchasing and imports ✅ done (closed properly on the second pass — see below)

**Q2 is answered** (§45 **D6**): landed cost is apportioned **by value**, remainder to the largest line.
The business's worked example became a test *before* the code, exactly as this plan demanded.

**Delivered (11 new tables, 53 in total):**

- **Purchase orders — optional, and meant it.** Requirements §25 says "PO is optional", so
  `goods_receipts.purchase_order_id` is nullable and the direct purchase (supplier → GRN → stock) is a
  first-class path. Forcing an order would only produce fakes raised after the fact, which look real
  and are therefore worse than none.
- **Goods receipts** with serial capture at the door — which is what makes P6's warranty claim
  answerable two years later.
- **Import shipments**, their charges (freight, insurance, customs, clearing, in any currency), and
  **landed cost**.
- **Supplier invoices and payments**, header + allocations: one transfer settles three invoices, one
  invoice is settled by two instalments. A single `invoice_id` on a payment expresses neither.
- **FX gain/loss on settlement.** A USD 1,000 invoice booked at 3.67 and paid at 3.60 leaves the shop
  AED 70 better off. That gain came from the currency moving, not from selling anything — so it is
  **not** folded back into the cost of the stock. The laptops did not become cheaper to buy.

**The hard part, and the thing to remember about this phase.** Goods and their true cost do not arrive
together: the container is unpacked in March and the clearing agent invoices in April. The receipt has
to post when the goods physically arrive, so the charges are folded in afterwards by a new
**`MovementType.Revaluation`** — money into stock, without inventing a unit. That forced a new column,
`stock_movements.value_adjustment`, and a corresponding change to the balance audit, which now
recomputes value as `SUM(quantity × unit_cost + value_adjustment)`. Leave that second term out and
every import the shop ever landed would show as a permanent discrepancy nobody could clear.

**When the stock has already gone**, the cost has nowhere to live: the container sold out before its
clearing invoice arrived. That money is not silently dropped (it would overstate margin) and not
smeared over whatever else is on the shelf (that would charge one container's freight to another's
goods). It is recorded as `import_shipments.unabsorbed_cost` — visible, attributable, and P7's to
expense.

**Tests: 173 → 231.** The D6 worked example is pinned twice: once as pure arithmetic, and once end to
end against a real database, where 10 laptops at 1,000 and 100 cables at 50 in a container carrying
AED 3,000 land at exactly 1,200 and 60. Also proven: the ledger still audits clean after a revaluation,
a container cannot be costed twice (which would double the freight inside a moving average, where it
would never wash out), and an unabsorbed remainder is reported rather than hidden.

**A P3 bug fell out of building this.** Adjustment and count handlers added each line to the DbSet *and*
to the parent's navigation collection; EF's fixup had already done the latter, so every total computed
from the in-memory graph — including an adjustment's `NetValue`, the money written off — was **double**.
The database was correct; the documents were not. Fixed, with a regression test.

#### P4 was marked done before it was ✅ closed for real

This phase claimed purchase orders, supplier invoices and supplier payments, and shipped **none of
them**. The domain entities and the eleven tables were real — `PurchaseOrder.Approve()`,
`SupplierInvoice.Post()`, `SupplierPaymentAllocation.ExchangeGainOrLoss` all existed and were correct —
but there was no command, no query, no handler and no endpoint above them. The only reachable purchasing
operations were `POST /goods-receipts` and the import-shipment flow. **The FX gain the phase describes in
detail had nowhere to be computed**, because nothing could record a supplier payment.

It went unnoticed because the phase's tests exercised what existed (landed cost, receiving) and the
screens that would have needed the rest were deferred with it. A phase is not done when its entities
compile; it is done when the shop can use it.

Now closed, and held to the same bar as P5:

- **Purchase orders** — create, approve, cancel, list. Approve is a separate permission because it is
  what commits the company's money, and it is the gate on receiving.
- **Supplier invoices** — record, post, cancel. Posting is what puts the debt on the supplier's balance;
  a draft owes nothing. It moves no stock, because the receipt already did.
- **Supplier payments** — header, allocations, and **the FX gain, realised at last**.
- **Read models** for all of the above, plus goods receipts, which previously could not even be listed.

**The arithmetic that had to be got right.** A USD 1,000 invoice booked at 3.67 is a debt of AED 3,670.
Paid at 3.60, only AED 3,600 leaves the bank. Subtract *what left the bank* from the supplier's balance —
the obvious implementation — and AED 70 sits there for ever, on an invoice that is fully settled in the
currency it was billed in. No amount of paying would ever clear it, because the residue is not a debt: it
is the gain. So a settled invoice has exactly what it *added* taken back off (`Amount × InvoiceRate`), and
the gain falls out as the difference — into the P&L, and **not** into the moving average. The laptops did
not become cheaper to buy; the currency moved.

**And a real bug, which only the live run found.** Receiving against an order ticked its lines off only
when the caller named `purchaseOrderLineId` explicitly. Name the *order* and nothing more — as any
reasonable client would — and the goods posted, the serials bound, and the order sat at `Approved` for
ever: fully delivered, still showing as outstanding, and chased. Nothing errored. The tests passed,
because they all named the line. An unnamed line is now matched on its product, and a genuinely ambiguous
order (two lines, one product) is reported rather than guessed at.

**Tests: 312 → 330**, and the whole path is proven against a live API: a container on the water, an order
approved, ten laptops and a hundred cables landed at the goods price, AED 3,000 of freight folded in to
give exactly 1,200 and 60, a USD invoice raised at 3.67, paid at 3.60 — AED 3,600 out of the bank, AED 70
of gain, supplier balance **zero**, stock still at 1,200, and the P3 balance audit still reconciling.

### P5 — Sales ✅ done

**Both blocking questions are answered** (§45 **D7**, **D8**): prices are **tax-exclusive** with
per-company effective-dated rates and **no jurisdiction hardcoded**; sales are raised in the company's
**base currency only**.

**Delivered (17 new tables, 70 in total):**

- **Quote → order → delivery → invoice.** A quotation reserves nothing (an offer is not a claim on the
  shelf, and holding stock for every speculative quote would empty the warehouse on paper while it sat
  full). **Confirming an order is what promises the stock** — one reservation per line, through the
  ledger, under its lock. That, and nothing else, is "prevent overselling".
- **The delivery is the only sales document that moves stock**, and it is where the serial binds. The
  invoice can be raised before the goods go, after them, or cover three deliveries — but exactly one
  document knows which machine went out of the door. **This is what makes P6's warranty claim answerable
  two years later.** The invoice snapshots the COGS the ledger valued the issue at: recomputing it later
  would restate the margin on every sale the shop has ever made, because the average keeps moving.
- **The till (POS)** — one call, one transaction, three documents. A declined card leaves the laptop on
  the shelf and no invoice chasing anybody. It composes the same handlers the documented flow uses, so
  there is still exactly one path by which stock moves.
- **Payments are header + tender + allocations.** One sale settled by cash *and* card; one transfer
  across three invoices; one invoice in two instalments. There is **no `Amount` column** on the payment —
  the total is the sum of the tender, because a header that could disagree with its method lines would be
  a till that does not balance.
- **Returns, credit notes, store credit.** A returned serial goes to `Returned`, **not** `InStock` — a
  machine that came back is inspected before it is sold to someone else. Store credit is a **ledger**, so
  "why do I have 240 credit?" has an answer.
- **Discount approval is a permission, not a queue** (§32). There is a customer at the counter; a sale
  that waited for a manager to log in would be a lost sale, and the shop would work around it by granting
  everyone the permission. Who approved it is stamped on the line.
- **`Idempotency-Key`** — the P4 debt, closed. The key is claimed by an INSERT *before* the work runs,
  and a unique index makes the claim atomic.

**The arithmetic that had to be got right, and the two places it was nearly got wrong:**

1. **Tax comes after the discount.** `SalesMath` is the only place that rule exists. Tax the gross and
   then discount, and every invoice the shop issues is wrong by the tax on the discount — a small number,
   on every line, forever, and one the tax authority eventually asks about.
2. **A refund is a negative payment, not just a negative invoice.** The credit note takes its total off
   the customer's balance; the refund then pays that credit back *out*, which puts it back on. Net zero.
   Only an offset against the balance actually moves it. Deduct on every method — the obvious
   implementation — and a customer refunded 100 in cash walks out with the money *and* a 100 credit.

**Tests: 231 → 312.** The end-to-end sale of a serial-tracked laptop is proven against a real Postgres:
stock falls by one, the serial is `Sold` and bound to its invoice line, COGS equals the moving average,
the customer owes 1,575 on a 1,500 sale, and **the P3 balance audit still reconciles** — which is what
proves nothing in sales wrote stock outside the ledger. Also proven: the same serial cannot be sold
twice, overselling is refused under the lock, a delivery cannot be billed twice, a declined card rolls
back all three documents, store credit cannot be overspent, and a replayed `Idempotency-Key` posts one
invoice rather than two.

**The screens.** `frontend/src/features/` existed as a README and nothing else; sales is the module that
finally creates it, so **the shape it lands on is the shape P6 and P7 will copy**:
`features/sales/{types, api, money-preview, components}`, with the routes in `app/` staying thin. Seven
screens — the till, quotations, orders, deliveries, invoices, payments, returns. The till is
barcode-first: the scan box holds focus and every scan adds a line, because a cashier's hands should
never have to leave the scanner.

Every state-changing call from the module sends an `Idempotency-Key`, generated **once per attempt**
rather than per render — a key that changed on re-render would be a new request every time, which is no
protection at all.

**Verified end to end against a live API**, not only by tests: a company was onboarded, two laptops
received at 1,200, and one sold through the till. The same idempotency key posted twice returned the
*identical receipt*, stock fell by exactly one, and exactly one invoice exists. The money came out
tax-exclusive (1,500 + 75 = 1,575, COGS 1,200, margin 300); the serial went to `Sold` and bound to its
invoice line; the return brought it back as `Returned` — **not** `InStock`, so it cannot be resold before
someone inspects it; the store credit landed in its ledger with the customer's balance at exactly zero;
and the P3 balance audit still agreed afterwards.

### P6 — Repairs ✅ done

**Three questions had to be answered before the phase could be built** (§45 **D9**, **D10**, **D11**):
when a part leaves stock, whether a warranty repair is free, and what a repair bill actually is. Each
is a one-line decision and each would have been expensive to reverse.

**Delivered (9 new tables, 79 in total):**

- **The job sheet, and the thing to hold on to about repairs: the device is not stock.** A customer's
  laptop on the workshop bench is not inventory — the shop does not own it, cannot sell it, and must not
  value it. So intake writes **no stock movement**. What moves is the parts fitted into it, and those
  leave the shelf through `IStockLedger` like everything else.
- **The workflow is a state machine, not a status column** (§28): `Received → Diagnosing →
  AwaitingApproval → InRepair → Testing → Ready → Delivered`, with every transition written to
  `repair_status_history` by the entity itself, so no handler can move a job and forget to say so.
- **The approval gate is the point of the whole workflow.** Nothing may be fitted to a machine whose
  owner has not agreed to the price — parts consumed against a job the customer then declines are parts
  the shop has paid for and cannot bill. **A warranty job skips the gate**, because there is no price for
  anyone to agree to; parking a free repair in "awaiting approval" would leave it waiting for a decision
  nobody was ever going to be asked to make.
- **Outsourced repair** (§29) — no stock moves (the device was never the shop's to move); what lands on
  the ticket is a *cost*, in the vendor's currency at the rate on the day.
- **Warranties and claims** (§30), and **the back-edge into Sales that this phase exists to prove**.

**The back-edge, which is what P5 was building all along.** A machine on the counter, two years later, is
traced to the sale that put it in the customer's hands: the serial → `Serial.SoldInvoiceLineId` → the
invoice line → the invoice. **Intake asks the warranty question itself and stamps the answer on the job.**
It is not a tickbox on a form, and that is deliberate — a tickbox is exactly how a shop ends up billing a
customer for a repair it had already promised to do for free. The lookup reads *two* sources, because the
system knows about warranties in two entirely different ways: the shop's own is **derived** (P5 computes
it at the moment of sale from the product's warranty months and stamps it on the unit — nothing registers
it), while a manufacturer's or a supplier's is **registered by hand**, because nobody can compute a term
somebody was given on paper. Where both cover the machine the *manufacturer's wins*: if someone else will
pay for the board, the shop should not be eating it out of its own provision. Getting that tie-break
backwards costs real money and nobody would ever notice.

**The arithmetic that had to be got right:**

1. **A warranty repair is costed, not free** (D10). The customer's bill is zero; the cost is not. The
   screen still left the shelf, so the job's gross profit comes out **negative — and it is supposed to**.
   A warranty repair that booked no cost would make warranty look free, and the shop would never learn
   which product line its warranty is quietly paying for. `is_chargeable` suppresses the *charge*, never
   the *cost*.
2. **Labour has no cost side, deliberately.** The technician's wage is a payroll expense (§34, P7), not a
   cost this job caused — the shop pays it whether the bench is busy or idle. Apportioning a salary across
   job sheets would invent a number the business never agreed to and make a quiet week show a loss on
   every ticket.
3. **The repair bill is an ordinary sales invoice** (D11), so it is paid, credited, chased and aged by
   machinery that already exists and is proven. It moves no stock and carries the cost the ledger reported
   when the part was *fitted* — not a fresh one, because by now the average has moved.

**A real EF trap, found by the tests and worth writing down.** `BaseEntity` assigns the primary key in its
initialiser, so a row discovered by EF through a **navigation collection** already has a key set — and EF
reads a set key as *"this row exists"*, tracks it as `Modified`, and issues an UPDATE against a row that
was never inserted. The status history would have vanished silently. This is the deeper reason behind the
rule P4 wrote down as a comment and P6 now understands: **through the DbSet, and only the DbSet.** The
domain still owns its history — `MoveTo` builds the row and appends it — but it *returns* it, and the
handler adds it.

**Tests: 330 → 358** (219 domain + 139 integration). Proven against a real Postgres: the part leaves the
shelf when it is fitted and not when it is billed; a warranty claim walks back to the invoice line that
sold the machine; a warranty job costs 120 and bills zero; rejecting a claim makes the job chargeable; a
returned part goes back with a real movement; a job cannot be billed twice; a declined estimate sends the
machine home with its serial back to `Sold` and **not** to `InStock` (which would put a customer's own
laptop back on the shelf to be sold to someone else); the cross-tenant gate covers all nine new tables;
**and the P3 balance audit still reconciles**, which is what proves repairs wrote no stock outside the
ledger.

**Verified end to end against a live API**, not only by tests — 22 checks, all green. A shop was onboarded,
a laptop sold, and the same machine brought back: the counter check answered *"Shop warranty, sold on
invoice INV-2026-00001, expires 14 Jul 2027"* without anybody telling it anything; the warranty job went
straight to the bench, consumed a screen, billed nothing and showed its −120; a chargeable job refused the
part until the customer approved, then came out at 500 charged, 120 cost, 380 margin, on a 525 invoice
(tax-exclusive, D7); an outsourced board came back at USD 100 × 3.67 = AED 367; the customer's balance
landed at exactly 2,100; and the balance audit still agreed afterwards.

**The screens.** `features/repairs/{types, api, components}` — the shape P5 set, copied as promised. Two:
the **workshop board** (open jobs first, *promised-date first within them*, because the job that is late is
the one the shop needs to see) and **warranties & claims**. The buttons on a job are the transitions it can
actually make — a status dropdown would let anyone move a job anywhere, and the rule that matters most
would be enforced by whoever remembered. The intake dialog checks the warranty **as the serial is typed**
and shows the answer in words the clerk can read out to the customer standing in front of them.

**Not built: repair photos** (§28). They are attachments, and where attachments live is **Q9**, which is
still open. Inventing a storage layer to answer it would be answering it. Recorded as debt below.

### P7 — Finance, reporting, dashboard 🟡 in progress

Receivables/payables ageing, statements, cash and bank accounts, expenses, the §35 report set, the
§36 dashboard, §37 global search.

- ✅ Resolved: **no general ledger** (§45 D3). P&L is computed (revenue − COGS − expenses) and the
  books export to an external accounting package. It will not reconcile line-for-line to an
  accountant's ledger — an accepted trade, recorded so nobody rediscovers it as a surprise.

#### Slice 1 — receivables, payables, statements ✅ done

**No new tables.** The documents that answer "who owes what" have all existed since P4 and P5; nothing was
missing but the arithmetic that reads them. Two new features (`reports.receivables`, `reports.payables`),
read-only — there is no `Create` on a report.

**The report's job is to prove the balance, not to restate it.** `Customer.Balance` and `Supplier.Balance`
are stored decimals, maintained by hand in eleven places between them, with no rebuild path and — until
now — nothing that ever summed the documents back to check. `Party.cs` has said since P2 that the balance
is "a cache of the ledger, and P7's receivables report must be able to prove it". So every report carries a
**variance**: the position rebuilt from the documents, minus the stored figure. Zero, or the shop is told.

Getting that zero is the whole of the work, because **the invoices and the balance disagree by
construction**, in two places, both deliberate and neither obvious:

1. **An offset credit note moves the balance and not the invoice.** Issuing one takes its total off
   `Customer.Balance` and writes no allocation row — so the invoice keeps its full `OutstandingAmount` and
   stays `Posted` for ever. **A report that aged the invoice alone would chase a customer, in the
   ninety-day column, for a debt they had already returned the goods for — and would go on doing it for the
   life of the system.** Verified against the live API: after the credit note the invoice still reads
   `Posted, outstanding 2,000`, and the ageing correctly reads zero.
2. **An unallocated payment moves the balance and not the invoice**, in the other direction. Money on
   account is shown as a credit against the customer rather than netted into the buckets, because someone
   who owes 10,000 at ninety days while sitting on a 9,000 advance is a different phone call from someone
   who owes 1,000.

Only an `OffsetAgainstBalance` credit note is netted, and the asymmetry is the point: a store-credit note
leaves the debt standing and hands over a voucher (a memo on the report, and a payment on the day it is
spent), and a cash refund is raised only against an invoice already paid. Neither moves the balance, so
netting either would break the very identity the report exists to demonstrate:

```
Σ (invoice outstanding − offset credit notes)  −  unallocated payments  =  Customer.Balance
Σ (invoice outstanding × the invoice's rate)   −  advances              =  Supplier.Balance
```

**A payable is valued at the rate its invoice was booked at, never today's.** It is the only valuation that
reconciles — the balance was raised at that rate and is discharged at that rate, which is what makes the
AED residue a *realised* gain when the money finally leaves (P4). Revaluing an open payable at spot would
book an **unrealised** gain, and this system has no such concept anywhere; inventing one inside a report
would be the wrong end of the system to invent it at. So the detail carries both numbers: what Shenzhen
will ask for (USD 1,000) and what it costs the shop (AED 3,670, at 3.67).

**Buckets are days overdue, not days since the invoice.** An invoice raised ninety days ago on sixty-day
terms is thirty days late, and a report that called it ninety would have the shop chasing a customer who is
behaving exactly as agreed. `DueAt` is null on a counter sale, and null means **due on receipt** — read as
"never due" it would drop every walk-in out of every bucket, which is to say it would under-report the debt
by precisely the sales least likely to be paid.

**Tests: 358 → 368.** Ten new, against a real Postgres, and the assertion that matters in every one of them
is `variance == 0`: with money on account, with an offset credit note, with a store-credit note that must
*not* net, with an advance to an overseas supplier, and with a foreign payable settled at a rate it was
never booked at. **Verified end to end against the live API**, not only by tests: a shop was onboarded, two
invoices raised, one paid in part and 800 left on account, a USD 1,000 bill booked at 3.67 and a USD 500
advance paid at 3.60 — receivables came out at 1,800 / 2,000 / 800 on account, net 3,000; payables at
AED 3,670 due less an AED 1,800 advance, net 1,870; and both variances zero.

**The screens.** `/reports/receivables` and `/reports/payables` — a bucket grid sorted by what is owed
rather than by name, because this is a screen somebody opens to decide who to telephone this morning. A
statement drawer behind each row opens the account: opening balance, every movement, closing balance, and
the running column their finance person will actually read.

#### Still to build

Cash and bank accounts, expenses (§34), the §35 report set including the computed P&L, the §36 dashboard,
§37 global search.

**A modelling gap this slice found and did not close.** A credit note does not write an allocation against
the invoice it credits, so the invoice can never reach `Paid` and its status is a lie the moment it is
credited. The reports work around it correctly, but *the invoice list still shows the invoice as unpaid and
overdue* — the workaround lives in the reports and does not reach the screens P5 built. Closing it properly
means a credit note allocating against the invoice the way a payment does, which is a change to settled,
proven P5 code and a migration, and it deserves its own decision rather than being smuggled in here.
Recorded as debt below.

### P8 — Platform / SaaS

Subscription plans, per-company entitlement enforcement, billing, invoices, platform admin console,
onboarding.

- 🟡 **Blocked on Q7** (payment processor; self-service card signup or manual onboarding?).
- Deliberately late, even though requirements puts SaaS management at §2. It is only worth building
  once tenants have something to pay for. The *data model* for subscriptions is cheap and can land
  earlier; the billing, enforcement and admin console wait.

### P9 — Hardening and integrations

Backup/restore (§43), rate limiting on `/auth/*`, Postgres row-level security, refresh token moved to
an httpOnly cookie, Excel import/export (§42), the integration surface (§44).

---

## Definition of done (every phase)

- Domain rules covered by unit tests; each endpoint covered by an integration test against a real
  Postgres (Testcontainers), **including the cross-tenant denial case**.
- Migration written, reviewed, and backward-compatible with the running release.
- OpenAPI document regenerated; the frontend's types match it.
- No secret in the repository; new configuration documented.
- Docs updated in the same PR as the code — a stale `api-design.md` is worse than none.

## Carried debt

Recorded rather than hidden. Each has an owning phase; none is a reason to stop.

| Item | Risk today | Close by |
| --- | --- | --- |
| Refresh token in `localStorage` | An XSS payload could read it. The access token is memory-only, so the blast radius is one refresh token, not a live session — but this is a weakness, not a clean design. | **P9** — httpOnly, SameSite cookie set by the API. |
| No rate limiting on `/auth/*` | Lockout blunts online brute force but not a distributed one. | **P9** |
| Migrations applied by the API at start-up (Development) | Fine for one developer; two production instances starting together would race. | Before the first real deploy: a separate deploy step. |
| Postgres RLS not enabled | The EF query filter is enforced centrally and proven by tests. RLS would stop even a raw SQL query that bypassed it. | Before production (database-design.md §1). |
| **Frontend types still hand-written** | `types/*.ts` mirror the API by hand. A renamed API field compiles fine on the client and fails at runtime. **P3 added the `codegen` script** that generates them from `/openapi/v1.json`; the hand-written types are still what the app imports, so the drift is now *checkable* but not yet *impossible*. Rolled from P4 (which built no screens), then from P5, and **P6 added a third hand-written module to the pile**. Every phase that ships screens without closing it makes it more expensive to close. | **P7**, and it should stop being deferred — it is the oldest live debt in the project. |
| **Repair photos not built** (§28) | A technician cannot attach the picture of the cracked screen the customer will later dispute — the one piece of evidence the intake notes exist to replace. This is a *scoped omission*, not an oversight: photos are attachments, and **where attachments live is Q9**, which is open. Building a storage layer to hold them would be answering Q9 by accident. | **Q9, then P9.** The rest of P6 does not depend on it. |
| **A credit note writes no allocation against the invoice it credits** | Found by P7 slice 1. Issuing one moves `Customer.Balance` but leaves the invoice at its full `OutstandingAmount` and `Posted` — so **a fully-credited invoice shows on the sales-invoice screen as unpaid and overdue, for ever**, and its status can never reach `Paid`. The receivables report nets credit notes per invoice and therefore reconciles, but the fix lives in the *report*, not in the model, so every other reader of the invoice still sees the wrong number. Closing it means a credit note allocating against the invoice the way a payment does — a change to proven P5 code plus a migration, and a decision worth taking on its own rather than smuggling into a reporting slice. | **P7**, before the dashboard reads invoice status. |
| ~~**P4 and P5 have no screens**~~ | **Closed.** Sales landed its seven screens in P5 slice 4; purchasing now has its five — goods receipts, orders, supplier invoices, supplier payments, imports and landed cost. Closing it surfaced the larger problem: P4 had no *endpoints* for half of what it claimed either (see P4 above). | ✅ |
| ~~Product delete does not check stock~~ | **Closed in P3.** Retiring a product now fails if it has stock on hand or live serials, and names the warehouses. | ✅ |
| ~~Update/delete missing on some reference data~~ | **Closed in P3.** Brands, tax rates, price tiers, payment methods and discounts can now be edited and retired. A tax rate's **percent** is deliberately not editable — changing it in place would restate the tax on invoices already issued — so `POST /tax-rates/{id}/supersede` closes the old rate and opens its successor. | ✅ |
| Migrations applied by the API at start-up (Development) | Two production instances starting together would race. The maintenance job is safe here (it only reads, and its writes are per-company and transactional), but the schema is not. | Before the first real deploy: a separate deploy step. |
| ~~No idempotency keys yet~~ | **Closed in P5.** `IdempotencyFilter` honours the header on every state-changing request: the key is claimed by an INSERT *before* the work runs, and a unique index makes the claim atomic, so two clicks 50ms apart cannot both sell the laptop. A retry replays the stored response; a failed request releases its key, because a mistyped payment that could never be re-sent would be a worse trap than the one being closed. | ✅ |
| ~~Repository not under version control~~ | **Closed.** The repository is on `origin` (GitHub) and P3 is pushed. The CI workflow's seven gates were verified locally before the push, so the pipeline is green the day it first runs rather than a wall of red everyone learns to ignore. | ✅ |

## Engineering practices

- **Branching:** short-lived branches off `main`, squash-merged. `main` is always deployable.
- **CI** (`.github/workflows/ci.yml`) — four gates on the backend, three on the frontend. All seven
  verified locally before being written down, so the pipeline is green the day it first runs rather
  than being a wall of red that everyone learns to ignore:

  | Gate | Why |
  | --- | --- |
  | `dotnet build -warnaserror` | A warning nobody fails on is a warning nobody reads — the EF-version conflict that cost an afternoon announced itself as one. |
  | `dotnet format --verify-no-changes` | Formatting arguments in review are wasted review. |
  | `dotnet test` | Includes the cross-tenant isolation suite against a real PostgreSQL (Testcontainers). |
  | `ef migrations has-pending-model-changes` | A model change with no migration builds and tests green, then fails on deploy against a schema that does not match. |
  | `npm ci` · `lint` · `build` | `ci` (not `install`) fails when package.json and the lockfile disagree — the bug you want to hear about. `next build` typechecks. |
- **Testing pyramid:** many domain unit tests, a solid layer of API integration tests against a real
  database, and a handful of end-to-end tests over the flows that lose money if they break (sell a
  serial-tracked item; receive an import and check the landed cost; complete a warranty repair).
- **Environments:** local (Docker) → staging → production. Migrations run as a separate deploy step,
  before the new app version starts.

## Local development

| Service  | URL |
| -------- | --- |
| Frontend | http://localhost:3000 |
| API      | http://localhost:5199 (`/health`, `/openapi/v1.json`) |
| Postgres | localhost:**5433** — db `techstorepro`, user `techstorepro` |

Port 5433, not 5432: a locally-installed PostgreSQL commonly owns 5432, and two servers fighting
over the port fails confusingly — the app connects successfully to the *wrong* database and reports
an authentication error.

### Configuration added in P3

```jsonc
"Inventory": {
  "Maintenance": {
    "Enabled": true,             // off ⇒ reservations are never swept and balances are never proven
    "SweepInterval": "00:15:00", // expired reservations; not nightly — a quote that expired at 09:00
                                 // must not hold the last unit off the shelf until 02:00 tomorrow
    "ReconcileAtUtcHour": 2      // the balance audit reads the whole ledger; not while the shop trades
  }
}
```

Turning `Enabled` off is a supported choice (a second instance should not run the job twice), and the
service logs a warning at start-up saying exactly what stops happening if you do.

## Immediate next steps

1. **P7 — finance, reporting, dashboard.** Nothing blocks it, and three of its inputs are already computed
   and waiting to be aggregated: P6's per-job margin (the §35 repair-profitability report is a `GROUP BY`
   over numbers that already exist), P4's `import_shipments.unabsorbed_cost`, and the cost of every
   warranty repair the shop has absorbed but never billed. All three are **expenses P7 owns**.
2. **Migrate the frontend onto generated types.** The `codegen` script has existed since P3 and nothing
   imports it. `features/{purchasing,sales,repairs}/types.ts` all mirror the API by hand, and a renamed
   field would compile clean and fail at runtime. **This is now the oldest live debt, and it grew again in
   P6** — every phase that ships screens without closing it makes it more expensive to close.
3. **Decide what a zero credit limit means.** `Customer.WouldExceedCreditLimit` treats zero as
   *unlimited*, while the entity's own comment says zero means "cash only". Nothing exercised it until
   P5. It is a one-line change either way, but it is a business decision, not a technical one.
4. **Answer Q9 (file storage), which now blocks a named feature.** §28's repair photos are the first thing
   the product has promised and not delivered because of it. Until it is answered, a technician cannot
   attach the picture of the cracked screen that the customer will later dispute.
5. **Audit P7 against the P4 lesson.** P4 was marked ✅ with half its use-case layer missing, and the docs
   asserted it for two phases. Before a phase is called done, check that every claim in its section has an
   endpoint behind it — not merely an entity. P6 was held to that bar: every claim above was exercised
   against a live API before this was written.

## Open questions that still block a phase

Only **Q7** (SaaS payment processor → P8) and **Q9** (deployment target and file storage → P9) remain.
Neither blocks P6.
