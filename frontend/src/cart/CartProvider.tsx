import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

export interface CartLine {
  productId: string;
  quantity: number;
}

interface CartContextValue {
  lines: CartLine[];
  /** Sepetteki toplam ürün adedi (menüdeki rozet için). */
  count: number;
  quantityOf: (productId: string) => number;
  setQuantity: (productId: string, quantity: number) => void;
  add: (productId: string, quantity?: number) => void;
  remove: (productId: string) => void;
  clear: () => void;
}

const STORAGE_KEY = "tezgah.cart";
const CartContext = createContext<CartContextValue | null>(null);

// Sepet tarayıcıda saklanır. Giriş yapmak için Keycloak'a gidip dönünce sayfa yeniden yüklenir;
// sepet bellekte dursaydı bu sırada kaybolurdu.
function loadCart(): CartLine[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(
      (l): l is CartLine =>
        typeof l?.productId === "string" && Number.isInteger(l?.quantity) && l.quantity > 0,
    );
  } catch {
    return [];
  }
}

export function CartProvider({ children }: { children: ReactNode }) {
  const [lines, setLines] = useState<CartLine[]>(loadCart);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(lines));
    } catch {
      // Gizli sekme gibi durumlarda kayıt yapılamayabilir; sepet yine de bu sekmede çalışır.
    }
  }, [lines]);

  const setQuantity = useCallback((productId: string, quantity: number) => {
    setLines((current) => {
      if (quantity <= 0) return current.filter((l) => l.productId !== productId);
      const exists = current.some((l) => l.productId === productId);
      return exists
        ? current.map((l) => (l.productId === productId ? { ...l, quantity } : l))
        : [...current, { productId, quantity }];
    });
  }, []);

  const value = useMemo<CartContextValue>(() => {
    const quantityOf = (productId: string) => lines.find((l) => l.productId === productId)?.quantity ?? 0;
    return {
      lines,
      count: lines.reduce((sum, l) => sum + l.quantity, 0),
      quantityOf,
      setQuantity,
      add: (productId, quantity = 1) => setQuantity(productId, quantityOf(productId) + quantity),
      remove: (productId) => setQuantity(productId, 0),
      clear: () => setLines([]),
    };
  }, [lines, setQuantity]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

export function useCart(): CartContextValue {
  const context = useContext(CartContext);
  if (!context) throw new Error("useCart, CartProvider içinde kullanılmalı.");
  return context;
}
