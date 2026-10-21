import { useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartProvider";
import { apiGet, apiPost } from "../lib/api";
import { formatMoney } from "../lib/format";
import type { Order, Product } from "../lib/types";
import { PageHeader } from "../components/PageHeader";
import { EmptyState } from "../components/EmptyState";
import { QuantityStepper } from "../components/QuantityStepper";

export function CartPage() {
  const cart = useCart();
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const products = useQuery({
    queryKey: ["products"],
    queryFn: ({ signal }) => apiGet<Product[]>("/api/products", signal),
  });

  const rows = cart.lines.flatMap((line) => {
    const product = products.data?.find((p) => p.productId === line.productId);
    return product ? [{ ...line, product }] : [];
  });
  const total = rows.reduce((sum, r) => sum + r.product.price * r.quantity, 0);

  // Idempotency-Key: Sepetin bu hali için tek bir anahtar. "Siparişi ver" isteği ağ hatası yüzünden
  // tekrar gönderilirse aynı anahtar gider ve sunucu ikinci bir sipariş açmaz. Sepet değişince yeni
  // bir sipariş sayılır, o yüzden anahtar da yenilenir.
  const signature = JSON.stringify(cart.lines);
  const idempotency = useRef({ signature, key: crypto.randomUUID() });
  if (idempotency.current.signature !== signature) {
    idempotency.current = { signature, key: crypto.randomUUID() };
  }

  const placeOrder = useMutation({
    mutationFn: () =>
      apiPost<Order>(
        "/api/orders",
        {
          // Yalnızca ürün ve adet gidiyor. customerId'yi gateway token'dan ekler,
          // fiyatı Inventory stok ayırırken belirler (ADR 0007).
          items: rows.map((r) => ({ productId: r.productId, quantity: r.quantity })),
        },
        { idempotencyKey: idempotency.current.key },
      ),
    onSuccess: ({ data }) => {
      cart.clear();
      void queryClient.invalidateQueries({ queryKey: ["orders"] });
      navigate(`/orders/${data.id}`);
    },
  });

  if (cart.lines.length === 0) {
    return (
      <>
        <PageHeader title="Sepet" />
        <EmptyState title="Sepetin boş">
          <Link to="/" className="font-semibold text-ink underline underline-offset-4">
            Ürünlere göz at
          </Link>
          , beğendiklerini sepete ekle.
        </EmptyState>
      </>
    );
  }

  return (
    <>
      <PageHeader title="Sepet" />

      <div className="grid gap-6 lg:grid-cols-[1fr_20rem] lg:items-start">
        <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface">
          {products.isPending && <li className="px-6 py-5 text-muted">Ürün bilgileri yükleniyor…</li>}
          {rows.map(({ productId, quantity, product }) => (
            <li key={productId} className="flex flex-wrap items-center gap-x-6 gap-y-3 px-5 py-5 sm:px-6">
              <div className="min-w-0 flex-1 basis-48">
                <p className="font-display text-lg font-bold leading-tight">{product.name}</p>
                <p className="mt-1 text-sm text-muted">Birim fiyat {formatMoney(product.price)}</p>
                {quantity > product.available && (
                  <p className="mt-1 text-sm font-semibold text-warn">
                    Stokta şu an {product.available} adet var. Sipariş verirsen stok yetmezse iptal edilir.
                  </p>
                )}
              </div>
              <QuantityStepper
                label={`${product.name} adedi`}
                value={quantity}
                max={Math.max(product.available, quantity)}
                onChange={(q) => cart.setQuantity(productId, q)}
              />
              <p className="w-28 text-right font-semibold tabular-nums">{formatMoney(product.price * quantity)}</p>
            </li>
          ))}
        </ul>

        <aside className="rounded-xl border border-line bg-surface p-6">
          <div className="flex items-baseline justify-between">
            <span className="text-muted">Toplam</span>
            <span className="font-display text-3xl font-bold tabular-nums">{formatMoney(total)}</span>
          </div>
          <p className="mt-2 text-sm text-muted">Kesin tutar, stok ayrılırken güncel fiyatlarla hesaplanır.</p>

          {user ? (
            <button
              type="button"
              onClick={() => placeOrder.mutate()}
              // İstek sürerken buton kilitlenir; çift tıklama ikinci isteği hiç göndermez.
              // Yine de bir istek tekrarlanırsa Idempotency-Key ikinci siparişi sunucuda engeller.
              disabled={placeOrder.isPending || rows.length === 0}
              className="mt-6 w-full rounded-md bg-ink px-5 py-3 text-[17px] font-semibold text-white hover:bg-ink/90 disabled:cursor-wait disabled:bg-ink/60"
            >
              {placeOrder.isPending ? "Sipariş gönderiliyor…" : "Siparişi ver"}
            </button>
          ) : (
            <button
              type="button"
              onClick={login}
              className="mt-6 w-full rounded-md bg-ink px-5 py-3 text-[17px] font-semibold text-white hover:bg-ink/90"
            >
              Sipariş vermek için giriş yap
            </button>
          )}

          {placeOrder.isError && (
            <p role="alert" className="mt-4 text-sm font-semibold text-bad">
              {placeOrder.error.message}
            </p>
          )}

          <p className="mt-4 text-xs leading-relaxed text-muted">
            Bu sipariş tek kullanımlık bir anahtarla gönderilir ({idempotency.current.key.slice(0, 8)}). Bağlantı
            kopup istek tekrar giderse ikinci bir sipariş oluşmaz.
          </p>
        </aside>
      </div>
    </>
  );
}
