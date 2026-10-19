namespace OrderService.Domain;

/// <summary>
/// Saga durum makinesi:
///
///   Pending ──StockReserved──► StockReserved ──PaymentCompleted──► Confirmed
///      │                           │
///      │ StockReservationFailed    │ PaymentFailed / zaman aşımı
///      ▼                           ▼
///   Cancelled ◄────────────────────┘
/// </summary>
public enum OrderStatus
{
    /// <summary>Sipariş alındı, stok bekleniyor.</summary>
    Pending,

    /// <summary>Stok ayrıldı, ödeme bekleniyor.</summary>
    StockReserved,

    /// <summary>Ödeme alındı, sipariş kesinleşti.</summary>
    Confirmed,

    /// <summary>Stok yok, ödeme başarısız ya da zaman aşımı.</summary>
    Cancelled
}
