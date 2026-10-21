import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { ApiError, apiGet } from "../lib/api";
import { formatMoney } from "../lib/format";
import type { Product } from "../lib/types";
import { useCart } from "../cart/CartProvider";
import { PageHeader } from "../components/PageHeader";
import { EmptyState } from "../components/EmptyState";
import { QuantityStepper } from "../components/QuantityStepper";

export function ProductsPage() {
  const products = useQuery({
    queryKey: ["products"],
    queryFn: ({ signal }) => apiGet<Product[]>("/api/products", signal),
    // Stok başka siparişlerle değişebilir; 5 saniyede bir yeniden çekiyoruz.
    refetchInterval: 5_000,
  });

  return (
    <>
      <PageHeader title="Ürünler">
        Stok sayıları depodan geliyor ve birkaç saniyede bir yenileniyor. Biri sipariş verdiğinde ayrılan
        adetler burada hemen düşer.
      </PageHeader>

      {products.isPending && <ProductListSkeleton />}

      {products.isError && (
        <EmptyState title="Ürünler yüklenemedi">
          {products.error instanceof ApiError && products.error.status === 0 ? (
            <>
              Gateway'e ulaşılamadı. Docker'ın açık olduğunu ve <code>api-gateway</code> servisinin çalıştığını
              <code> docker compose ps</code> ile kontrol et.
            </>
          ) : (
            products.error.message
          )}
        </EmptyState>
      )}

      {products.isSuccess && products.data.length === 0 && (
        <EmptyState title="Henüz ürün yok">Inventory Service'e ürün eklendiğinde burada görünecek.</EmptyState>
      )}

      {products.isSuccess && products.data.length > 0 && (
        <>
          <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface">
            {products.data.map((product) => (
              <ProductRow key={product.productId} product={product} />
            ))}
          </ul>
          <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-muted">
              Son güncelleme: {new Date(products.dataUpdatedAt).toLocaleTimeString("tr-TR")}
            </p>
            <CartShortcut />
          </div>
        </>
      )}
    </>
  );
}

function ProductRow({ product }: { product: Product }) {
  const cart = useCart();
  const inCart = cart.quantityOf(product.productId);
  const stock = stockStatus(product.available);

  return (
    <li className="flex flex-wrap items-center gap-x-6 gap-y-4 px-5 py-5 sm:px-6">
      <div className="min-w-0 flex-1 basis-56">
        <p className="font-display text-xl font-bold leading-tight">{product.name}</p>
        <p className="mt-1 text-sm text-muted">Stok kodu {product.sku}</p>
        <p className="mt-2 text-[17px] font-semibold tabular-nums">{formatMoney(product.price)}</p>
      </div>

      <div className="w-28 text-right">
        <p className="font-display text-3xl font-bold tabular-nums leading-none">{product.available}</p>
        <p className={`mt-1.5 text-sm font-semibold ${stock.className}`}>{stock.label}</p>
        {product.reserved > 0 && <p className="mt-0.5 text-xs text-muted">{product.reserved} adet ayrıldı</p>}
      </div>

      <div className="flex w-36 justify-end">
        {inCart > 0 ? (
          <QuantityStepper
            label={`${product.name} adedi`}
            value={inCart}
            // Stoktan fazlası sepete eklenebilseydi sipariş saga'da iptal olurdu; burada önlüyoruz.
            max={product.available}
            onChange={(q) => cart.setQuantity(product.productId, q)}
          />
        ) : (
          <button
            type="button"
            onClick={() => cart.add(product.productId)}
            disabled={product.available === 0}
            className="rounded-md bg-ink px-4 py-2 text-[15px] font-semibold text-white hover:bg-ink/90 disabled:cursor-not-allowed disabled:bg-line disabled:text-muted"
          >
            Sepete ekle
          </button>
        )}
      </div>
    </li>
  );
}

function CartShortcut() {
  const { count } = useCart();
  if (count === 0) return null;
  return (
    <Link to="/cart" className="rounded-md bg-tag px-4 py-2 font-semibold text-ink hover:bg-tag/85">
      Sepete git ({count} ürün)
    </Link>
  );
}

function stockStatus(available: number) {
  if (available === 0) return { label: "Tükendi", className: "text-bad" };
  if (available <= 5) return { label: `Son ${available} adet`, className: "text-warn" };
  return { label: "Stokta", className: "text-ok" };
}

function ProductListSkeleton() {
  return (
    <ul aria-label="Ürünler yükleniyor" className="divide-y divide-line rounded-xl border border-line bg-surface">
      {[0, 1, 2].map((i) => (
        <li key={i} className="flex items-center gap-6 px-6 py-5">
          <div className="flex-1 space-y-2">
            <div className="h-5 w-48 animate-pulse rounded bg-canvas" />
            <div className="h-4 w-28 animate-pulse rounded bg-canvas" />
          </div>
          <div className="h-8 w-12 animate-pulse rounded bg-canvas" />
        </li>
      ))}
    </ul>
  );
}
