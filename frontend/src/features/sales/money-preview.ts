/**
 * The till's running total, computed in the browser.
 *
 * **This is a preview, and the server is the authority.** `SalesMath` on the backend decides what the
 * customer is actually charged; this exists only so the cashier sees a number update as they scan,
 * rather than watching a spinner. The receipt shows the figures the API came back with, not these.
 *
 * It must nonetheless follow the same rule as D7 — **discount first, then tax, rounded at the line** —
 * because a preview that disagrees with the printed invoice is worse than no preview at all: the
 * customer sees one number on the screen and another on the paper, and neither the cashier nor the
 * system can explain which is right.
 */

export function round(amount: number) {
  // Away from zero at the half, matching MidpointRounding.AwayFromZero on the server. Math.round()
  // already rounds .5 up for positives, which is every figure a till ever shows.
  return Math.round((amount + Number.EPSILON) * 100) / 100;
}

export function lineNet(quantity: number, unitPrice: number, discountPercent: number) {
  const gross = round(quantity * unitPrice);

  return Math.max(0, round(gross - round((gross * discountPercent) / 100)));
}

export function lineTax(net: number, taxPercent: number) {
  return round((net * taxPercent) / 100);
}
