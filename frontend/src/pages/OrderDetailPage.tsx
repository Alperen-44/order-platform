import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { ApiError, apiGet } from "../lib/api";
import { formatDateTime, formatMoney, orderStatusText, shortId } from "../lib/format";
import { useProductNames } from "../lib/useProductNames";
import type { Order, Payment } from "../lib/types";
import { EmptyState } from "../components/EmptyState";
import { LoginPrompt } from "../components/LoginPrompt";
import { OrderTimeline } from "../components/OrderTimeline";

const isFinal = (order?: Order) => order?.status === "Confirmed" || order?.status === "Cancelled";

/** Pending'de fiyat henüz yok; iptal edilen siparişte de stok ayrılamadıysa fiyat hiç belirlenmedi. */
const isPriced = (order: Order) => order.status !== "Pending" && order.totalAmount > 0;

export function OrderDetailPage() {
  const { user } = useAuth();
  if (!user) {
    return <LoginPrompt title="Siparişi görmek için giriş yap">Sipariş detayları yalnızca sahibine görünür.</LoginPrompt>;
  }
  return <OrderDetail />;
}

function OrderDetail() {
  const { orderId = "" } = useParams();
  const productName = useProductNames();

  const order = useQuery({
    queryKey: ["orders", orderId],
    queryFn: ({ signal }) => apiGet<Order>(`/api/orders/${orderId}`, signal),
    // Saga sürerken her saniye yenile; sipariş onaylanınca ya da iptal edilince dur.
    refetchInterval: (query) => (isFinal(query.state.data) ? false : 1_000),
  });

  const payment = usePayment(orderId, order.data);

  if (order.isPending) return <p className="text-muted">Sipariş yükleniyor…</p>;
  if (order.isError) {
    // Başkasının siparişi de 404 döner: sunucu o siparişin var olup olmadığını bile söylemez.
    return (
      <EmptyState title="Sipariş bulunamadı">
        {order.error.message}{" "}
        <Link to="/orders" className="font-semibold text-ink underline underline-offset-4">
          Siparişlerine dön
        </Link>
      </EmptyState>
    );
  }

  const data = order.data;
  const status = orderStatusText[data.status];

  return (
    <>
      <Link to="/orders" className="text-[15px] font-medium text-muted hover:text-ink">
        ← Siparişlerim
      </Link>

      <div className="mt-4 mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-display text-4xl font-bold tracking-tight sm:text-5xl">Sipariş {shortId(data.id)}</h1>
          <p className="mt-2 text-muted">{formatDateTime(data.createdAt)}</p>
        </div>
        <span className={`rounded-full px-4 py-1.5 font-semibold ${status.className}`}>{status.label}</span>
      </div>

      <div className="grid gap-6 lg:grid-cols-[1fr_20rem] lg:items-start">
        <section aria-labelledby="timeline-title" className="rounded-xl border border-line bg-surface p-6 sm:p-8">
          <h2 id="timeline-title" className="sr-only">
            Sipariş adımları
          </h2>
          <OrderTimeline order={data} payment={payment} />
          {!isFinal(data) && (
            <p className="mt-6 text-sm text-muted">Bu sayfa sipariş sonuçlanana kadar her saniye kendini yeniler.</p>
          )}
        </section>

        <aside className="rounded-xl border border-line bg-surface p-6">
          <h2 className="font-display text-lg font-bold">Ürünler</h2>
          <ul className="mt-3 space-y-2">
            {data.items.map((item) => (
              <li key={item.productId} className="flex justify-between gap-4 text-[15px]">
                <span>
                  {item.quantity} × {productName(item.productId)}
                </span>
                <span className="tabular-nums">
                  {isPriced(data) ? formatMoney(item.unitPrice * item.quantity) : "—"}
                </span>
              </li>
            ))}
          </ul>
          <div className="mt-4 flex items-baseline justify-between border-t border-line pt-4">
            <span className="text-muted">Toplam</span>
            <span className="font-display text-2xl font-bold tabular-nums">
              {isPriced(data) ? formatMoney(data.totalAmount) : "—"}
            </span>
          </div>
          {data.status === "Pending" && (
            <p className="mt-2 text-sm text-muted">Tutar, stok ayrılınca güncel fiyatlarla hesaplanacak.</p>
          )}
          <PaymentSummary payment={payment} />
        </aside>
      </div>
    </>
  );
}

/**
 * Ödeme kaydı Payment Service'te, siparişten ayrı tutuluyor. Henüz ödeme yoksa 404 döner (null).
 * Ödeme sonuçlanana kadar 2 saniyede bir yeniler. Zaman aşımıyla iptal edilen siparişte ödeme
 * sonradan tamamlanıp iade edilebildiği için iptalden sonra da bir süre izlemeye devam eder.
 */
function usePayment(orderId: string, order?: Order): Payment | null | undefined {
  const reachedPayment = order !== undefined && order.status !== "Pending" && order.totalAmount > 0;

  const payment = useQuery({
    queryKey: ["payments", orderId],
    enabled: reachedPayment,
    queryFn: async ({ signal }) => {
      try {
        return await apiGet<Payment>(`/api/payments/${orderId}`, signal);
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null;
        throw error;
      }
    },
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (status === "Failed" || status === "Refunded") return false;
      if (status === "Completed" && order?.status === "Confirmed") return false;
      const sinceUpdate = order ? Date.now() - new Date(order.updatedAt).getTime() : 0;
      return sinceUpdate < 60_000 ? 2_000 : false;
    },
  });

  return payment.data;
}

const paymentText: Record<Payment["status"], { label: string; className: string }> = {
  Completed: { label: "Ödeme alındı", className: "text-ok" },
  Failed: { label: "Ödeme reddedildi", className: "text-bad" },
  Refunded: { label: "Ödeme iade edildi", className: "text-warn" },
};

function PaymentSummary({ payment }: { payment: Payment | null | undefined }) {
  if (!payment) return null;
  const text = paymentText[payment.status];

  return (
    <div className="mt-4 border-t border-line pt-4 text-[15px]">
      <p className={`font-semibold ${text.className}`}>{text.label}</p>
      {payment.providerReference && <p className="mt-1 text-sm text-muted">İşlem no {payment.providerReference}</p>}
      {payment.failureReason && <p className="mt-1 text-sm text-muted">Sağlayıcı yanıtı: {payment.failureReason}</p>}
    </div>
  );
}
