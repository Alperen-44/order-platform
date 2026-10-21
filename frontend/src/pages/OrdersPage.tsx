import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { apiGet } from "../lib/api";
import { formatDateTime, formatMoney, orderStatusText, shortId } from "../lib/format";
import { useProductNames } from "../lib/useProductNames";
import type { Order } from "../lib/types";
import { PageHeader } from "../components/PageHeader";
import { EmptyState } from "../components/EmptyState";
import { LoginPrompt } from "../components/LoginPrompt";

export function OrdersPage() {
  const { user } = useAuth();

  return (
    <>
      <PageHeader title="Siparişlerim">
        {user && "Yalnızca senin verdiğin siparişler listelenir. Durumlar birkaç saniyede bir güncellenir."}
      </PageHeader>
      {user ? (
        <OrderList />
      ) : (
        <LoginPrompt title="Siparişlerini görmek için giriş yap">
          Giriş yaptıktan sonra verdiğin siparişler ve her birinin son durumu burada listelenir.
        </LoginPrompt>
      )}
    </>
  );
}

function OrderList() {
  const productName = useProductNames();
  const orders = useQuery({
    queryKey: ["orders"],
    queryFn: ({ signal }) => apiGet<Order[]>("/api/orders", signal),
    refetchInterval: 5_000,
  });

  if (orders.isPending) return <p className="text-muted">Siparişler yükleniyor…</p>;
  if (orders.isError) return <EmptyState title="Siparişler yüklenemedi">{orders.error.message}</EmptyState>;
  if (orders.data.length === 0) {
    return (
      <EmptyState title="Henüz siparişin yok">
        Ürünler sayfasından sipariş verdiğinde burada görünecek.
      </EmptyState>
    );
  }

  return (
    <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface">
      {orders.data.map((order) => {
        const status = orderStatusText[order.status];
        return (
          <li key={order.id} className="flex flex-wrap items-start gap-x-6 gap-y-2 px-5 py-5 sm:px-6">
            <div className="min-w-0 flex-1">
              <Link
                to={`/orders/${order.id}`}
                className="font-display text-lg font-bold underline-offset-4 hover:underline"
              >
                Sipariş {shortId(order.id)}
              </Link>
              <p className="mt-1 text-sm text-muted">{formatDateTime(order.createdAt)}</p>
              <ul className="mt-2 text-[15px]">
                {order.items.map((item) => (
                  <li key={item.productId}>
                    {item.quantity} × {productName(item.productId)}
                  </li>
                ))}
              </ul>
              {order.cancellationReason && (
                <p className="mt-2 text-sm text-bad">Sebep: {order.cancellationReason}</p>
              )}
            </div>
            <div className="text-right">
              <span className={`inline-block rounded-full px-3 py-1 text-sm font-semibold ${status.className}`}>
                {status.label}
              </span>
              <p className="mt-2 font-display text-lg font-bold tabular-nums">{formatMoney(order.totalAmount)}</p>
            </div>
          </li>
        );
      })}
    </ul>
  );
}
