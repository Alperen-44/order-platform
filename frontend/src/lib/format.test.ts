import { describe, expect, it } from "vitest";
import { cancellation, shortId } from "./format";

describe("cancellation", () => {
  // Backend iptal sebebini "Stock: ..." / "Payment: ..." / "Payment timeout: ..." diye yazıyor.
  it.each([
    ["Stock: Insufficient stock for product x (requested 1000)", "stock", "Yeterli stok yoktu"],
    ["Payment: Card declined (test customer)", "payment", "Ödeme reddedildi"],
    ["Payment timeout: no payment result within 15s", "payment", "zamanında tamamlanmadı"],
  ])("%s → %s adımı", (reason, step, text) => {
    const result = cancellation(reason);
    expect(result.step).toBe(step);
    expect(result.text).toContain(text);
  });

  it("sebep yoksa ödeme adımında durmuş sayılır", () => {
    expect(cancellation(null).step).toBe("payment");
  });
});

describe("shortId", () => {
  it("sipariş numarasının ilk 8 karakterini büyük harfle gösterir", () => {
    expect(shortId("0df1fa89-4d46-4aeb-8b37-37c2cbb84b7e")).toBe("0DF1FA89");
  });
});
