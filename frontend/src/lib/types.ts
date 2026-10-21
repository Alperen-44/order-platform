// Backend'in döndürdüğü JSON'ların TypeScript karşılıkları.
// Alan adları ASP.NET Core'un varsayılan camelCase çıktısıyla aynı.

/** inventory-service → ProductResponse */
export interface Product {
  productId: string;
  sku: string;
  name: string;
  /** Birim fiyat (TL) */
  price: number;
  available: number;
  reserved: number;
  updatedAt: string;
}

/** order-service → OrderStatus (saga'nın adımları) */
export type OrderStatus = "Pending" | "StockReserved" | "Confirmed" | "Cancelled";

export interface OrderItem {
  productId: string;
  quantity: number;
  /** Stok ayrılana kadar 0: fiyatı Inventory belirler. */
  unitPrice: number;
}

/** order-service → OrderResponse */
export interface Order {
  id: string;
  customerId: string;
  status: OrderStatus;
  totalAmount: number;
  cancellationReason: string | null;
  createdAt: string;
  updatedAt: string;
  items: OrderItem[];
}

/** notification-service → Notification */
export interface Notification {
  id: string;
  orderId: string;
  customerId: string;
  type: string;
  message: string;
  createdAt: string;
}
