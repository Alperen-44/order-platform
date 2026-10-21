import type { OrderStatus } from "./types";

const currency = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
const dateTime = new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short" });
const time = new Intl.DateTimeFormat("tr-TR", { timeStyle: "medium" });

export const formatMoney = (amount: number) => currency.format(amount);
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));
export const formatTime = (iso: string) => time.format(new Date(iso));

/** Sipariş numarasının ilk 8 karakteri: kullanıcıya uzun GUID göstermek yerine. */
export const shortId = (id: string) => id.slice(0, 8).toUpperCase();

export const orderStatusText: Record<OrderStatus, { label: string; className: string }> = {
  Pending: { label: "Stok kontrol ediliyor", className: "bg-canvas text-muted" },
  StockReserved: { label: "Ödeme bekleniyor", className: "bg-tag/25 text-warn" },
  Confirmed: { label: "Onaylandı", className: "bg-ok/12 text-ok" },
  Cancelled: { label: "İptal edildi", className: "bg-bad/10 text-bad" },
};

/** Saga hangi adımda durduysa onu söyler. Backend sebebi "Stock: ..." ya da "Payment: ..." diye yazar. */
export type FailedStep = "stock" | "payment";

export function cancellation(reason: string | null): { step: FailedStep; text: string } {
  const r = reason ?? "";
  if (r.startsWith("Stock:")) return { step: "stock", text: "Yeterli stok yoktu, sipariş iptal edildi." };
  if (r.startsWith("Payment timeout")) {
    return { step: "payment", text: "Ödeme zamanında tamamlanmadı, sipariş iptal edildi." };
  }
  return { step: "payment", text: "Ödeme reddedildi, sipariş iptal edildi." };
}
