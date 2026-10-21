import { describe, expect, it } from "vitest";
import { timelineSteps } from "./OrderTimeline";
import type { Order, OrderStatus, Payment } from "../lib/types";

// Saga'nın her sonucunun ekranda doğru adımlara dönüştüğünü kontrol eder.
// timelineSteps saf bir fonksiyon (aynı girdi → aynı çıktı), bu yüzden tarayıcı gerekmeden test edilir.

function order(status: OrderStatus, cancellationReason: string | null = null): Order {
  return {
    id: "0df1fa89-4d46-4aeb-8b37-37c2cbb84b7e",
    customerId: "791b9e7a-a0ed-4c52-8c49-2d7dd1267c75",
    status,
    totalAmount: status === "Pending" ? 0 : 1500,
    cancellationReason,
    createdAt: "2026-10-21T10:00:00Z",
    updatedAt: "2026-10-21T10:00:05Z",
    items: [{ productId: "11111111-1111-1111-1111-111111111111", quantity: 1, unitPrice: 1500 }],
  };
}

function payment(status: Payment["status"]): Payment {
  return {
    id: "p1",
    orderId: "0df1fa89-4d46-4aeb-8b37-37c2cbb84b7e",
    amount: 1500,
    status,
    providerReference: status === "Failed" ? null : "pay_123",
    failureReason: status === "Failed" ? "Card declined (test customer)" : null,
    createdAt: "2026-10-21T10:00:25Z",
    updatedAt: "2026-10-21T10:00:26Z",
  };
}

const states = (o: Order, p?: Payment | null) => timelineSteps(o, p).map((s) => s.state);
const titles = (o: Order, p?: Payment | null) => timelineSteps(o, p).map((s) => s.title);

describe("timelineSteps", () => {
  it("Pending: sipariş alındı, stok kontrol ediliyor", () => {
    expect(states(order("Pending"))).toEqual(["done", "active", "waiting", "waiting"]);
  });

  it("StockReserved: stok ayrıldı, ödeme bekleniyor", () => {
    expect(states(order("StockReserved"))).toEqual(["done", "done", "active", "waiting"]);
  });

  it("Confirmed: bütün adımlar tamamlandı", () => {
    const o = order("Confirmed");
    expect(states(o)).toEqual(["done", "done", "done", "done"]);
    expect(titles(o).at(-1)).toBe("Sipariş onaylandı");
  });

  it("stok yetmediyse ödeme hiç istenmez", () => {
    const o = order("Cancelled", "Stock: Insufficient stock for product 1111 (requested 1000)");
    expect(states(o)).toEqual(["done", "failed", "skipped", "failed"]);
    expect(titles(o)[1]).toBe("Stok ayrılamadı");
  });

  it("ödeme reddedildiyse stok ayrılmış ama geri bırakılmıştır", () => {
    const steps = timelineSteps(order("Cancelled", "Payment: Card declined (test customer)"), payment("Failed"));
    expect(steps.map((s) => s.state)).toEqual(["done", "done", "failed", "failed"]);
    expect(steps[1].note).toContain("geri bırakıldı");
    expect(steps[2].title).toBe("Ödeme alınamadı");
    expect(steps[2].note).toContain("reddedildi");
  });

  it("zaman aşımında ödeme henüz gelmediyse iade adımı yoktur", () => {
    const o = order("Cancelled", "Payment timeout: no payment result within 15s");
    expect(timelineSteps(o, null)).toHaveLength(4);
    expect(timelineSteps(o, null)[2].note).toContain("zamanında tamamlanmadı");
  });

  it("zaman aşımından sonra gelen ödeme iade edildiyse 5. adım eklenir", () => {
    const steps = timelineSteps(
      order("Cancelled", "Payment timeout: no payment result within 15s"),
      payment("Refunded"),
    );
    expect(steps).toHaveLength(5);
    expect(steps[4]).toMatchObject({ title: "Geç gelen ödeme iade edildi", state: "done" });
  });
});
