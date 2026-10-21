import { useQuery } from "@tanstack/react-query";
import { apiGet } from "./api";
import type { Product } from "./types";

/**
 * Sipariş kalemlerinde yalnızca productId var; ürün adını göstermek için ürün listesini kullanıyoruz.
 * Ürünler sayfasıyla aynı queryKey: veri zaten önbellekteyse tekrar istek atılmaz.
 */
export function useProductNames(): (productId: string) => string {
  const products = useQuery({
    queryKey: ["products"],
    queryFn: ({ signal }) => apiGet<Product[]>("/api/products", signal),
  });

  return (productId) =>
    products.data?.find((p) => p.productId === productId)?.name ?? "Ürün";
}
