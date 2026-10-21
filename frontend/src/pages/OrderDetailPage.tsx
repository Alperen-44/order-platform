import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { apiGet } from "../lib/api";
import { formatDateTime, formatMoney, orderStatusText, shortId } from "../lib/format";
import { useProductNames } from "../lib/useProductNames";
import type { Order } from "../lib/types";
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
          <OrderTimeline order={data} />
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
        </aside>
      </div>
    </>
  );
}
