import { cancellation, formatTime } from "../lib/format";
import type { Order, Payment } from "../lib/types";

type StepState = "done" | "active" | "failed" | "skipped" | "waiting";

interface Step {
  title: string;
  state: StepState;
  note?: string;
}

/**
 * Saga'nın adımlarını siparişin anlık durumundan çıkarır.
 *
 *   Pending        → stok kontrol ediliyor
 *   StockReserved  → stok ayrıldı, ödeme bekleniyor
 *   Confirmed      → hepsi tamam
 *   Cancelled      → sebebe göre stokta ya da ödemede durdu
 *
 * Bu fonksiyon saf (yan etkisiz) olduğu için birim testi kolay: 8.7'de test edeceğiz.
 */
export function timelineSteps(order: Order, payment?: Payment | null): Step[] {
  const received: Step = { title: "Sipariş alındı", state: "done", note: formatTime(order.createdAt) };

  switch (order.status) {
    case "Pending":
      return [
        received,
        { title: "Stok ayrılıyor", state: "active", note: "Inventory Service stoğu kontrol ediyor" },
        { title: "Ödeme", state: "waiting" },
        { title: "Onay", state: "waiting" },
      ];
    case "StockReserved":
      return [
        received,
        { title: "Stok ayrıldı", state: "done", note: "Tutar güncel fiyatlarla hesaplandı" },
        { title: "Ödeme alınıyor", state: "active", note: "Payment Service ödemeyi işliyor" },
        { title: "Onay", state: "waiting" },
      ];
    case "Confirmed":
      return [
        received,
        { title: "Stok ayrıldı", state: "done" },
        { title: "Ödeme alındı", state: "done" },
        { title: "Sipariş onaylandı", state: "done", note: formatTime(order.updatedAt) },
      ];
    case "Cancelled": {
      const { step, text } = cancellation(order.cancellationReason);
      if (step === "stock") {
        return [
          received,
          { title: "Stok ayrılamadı", state: "failed", note: text },
          { title: "Ödeme", state: "skipped", note: "Stok olmadığı için ödeme hiç istenmedi" },
          { title: "Sipariş iptal edildi", state: "failed", note: formatTime(order.updatedAt) },
        ];
      }
      const steps: Step[] = [
        received,
        { title: "Stok ayrıldı", state: "done", note: "Ödeme alınamayınca ayrılan stok depoya geri bırakıldı" },
        { title: "Ödeme alınamadı", state: "failed", note: text },
        { title: "Sipariş iptal edildi", state: "failed", note: formatTime(order.updatedAt) },
      ];
      // Zaman aşımından SONRA ödeme yine de tamamlanabilir (sağlayıcı geç cevap verdi).
      // Payment Service OrderCancelled olayını görünce parayı otomatik iade eder: saga'nın telafi adımı.
      if (payment?.status === "Refunded") {
        steps.push({
          title: "Geç gelen ödeme iade edildi",
          state: "done",
          note: `${formatTime(payment.updatedAt)}, sipariş iptal olduğu için Payment Service parayı geri verdi`,
        });
      }
      return steps;
    }
  }
}

const marker: Record<StepState, string> = {
  done: "bg-ok border-ok text-white",
  active: "bg-tag border-tag text-ink",
  failed: "bg-bad border-bad text-white",
  skipped: "bg-surface border-line text-muted",
  waiting: "bg-surface border-line text-muted",
};

const symbol: Record<StepState, string> = {
  done: "✓",
  active: "",
  failed: "✕",
  skipped: "–",
  waiting: "",
};

export function OrderTimeline({ order, payment }: { order: Order; payment?: Payment | null }) {
  const steps = timelineSteps(order, payment);

  return (
    <ol className="relative" aria-label="Sipariş adımları">
      {steps.map((step, index) => {
        const last = index === steps.length - 1;
        return (
          <li key={step.title} className="relative flex gap-4 pb-8 last:pb-0">
            {!last && (
              <span
                aria-hidden
                className={`absolute left-[15px] top-8 h-[calc(100%-2rem)] w-0.5 ${
                  step.state === "done" ? "bg-ok" : "bg-line"
                }`}
              />
            )}
            <span
              aria-hidden
              className={`relative z-10 grid h-8 w-8 shrink-0 place-items-center rounded-full border-2 text-sm font-bold ${marker[step.state]}`}
            >
              {step.state === "active" ? (
                <span className="h-2.5 w-2.5 animate-ping rounded-full bg-ink" />
              ) : (
                symbol[step.state]
              )}
            </span>
            <div className="pt-0.5">
              <p
                className={`font-display text-lg font-bold leading-tight ${
                  step.state === "waiting" || step.state === "skipped" ? "text-muted" : ""
                }`}
              >
                {step.title}
                <span className="sr-only"> ({stateLabel[step.state]})</span>
              </p>
              {step.note && <p className="mt-1 text-[15px] text-muted">{step.note}</p>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

const stateLabel: Record<StepState, string> = {
  done: "tamamlandı",
  active: "sürüyor",
  failed: "başarısız",
  skipped: "atlandı",
  waiting: "bekliyor",
};
