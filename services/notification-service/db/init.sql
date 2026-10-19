-- Notification servisinin şeması. Container ilk kez oluşturulurken çalışır.

CREATE TABLE notifications (
    id           UUID PRIMARY KEY,
    order_id     UUID          NOT NULL,
    customer_id  UUID          NOT NULL,
    type         VARCHAR(100)  NOT NULL,   -- OrderConfirmed | OrderCancelled | PaymentRefunded
    message      VARCHAR(1000) NOT NULL,
    created_at   TIMESTAMPTZ   NOT NULL
);

CREATE INDEX ix_notifications_order_id ON notifications (order_id);

-- Idempotent consumer kaydı: aynı olay iki kez gelse de müşteriye iki bildirim gitmez.
CREATE TABLE processed_events (
    consumer_group  VARCHAR(100) NOT NULL,
    event_id        UUID         NOT NULL,
    event_type      VARCHAR(100) NOT NULL,
    processed_at    TIMESTAMPTZ  NOT NULL,
    PRIMARY KEY (consumer_group, event_id)
);
