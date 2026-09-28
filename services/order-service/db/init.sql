-- Order servisinin şeması. Container ilk kez oluşturulurken çalışır.
-- (Şema değişikliklerini ileride EF Core migration'larına taşıyacağız; bkz. docs/adr/0002)

CREATE TABLE orders (
    id            UUID PRIMARY KEY,
    customer_id   UUID           NOT NULL,
    status        VARCHAR(32)    NOT NULL,
    total_amount  NUMERIC(12, 2) NOT NULL CHECK (total_amount >= 0),
    created_at    TIMESTAMPTZ    NOT NULL,
    updated_at    TIMESTAMPTZ    NOT NULL
);

CREATE TABLE order_items (
    id          UUID PRIMARY KEY,
    order_id    UUID           NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    product_id  UUID           NOT NULL,
    quantity    INT            NOT NULL CHECK (quantity > 0),
    unit_price  NUMERIC(12, 2) NOT NULL CHECK (unit_price >= 0)
);

CREATE INDEX ix_order_items_order_id ON order_items (order_id);

-- Outbox: sipariş ile "OrderCreated" olayı AYNI transaction içinde yazılır.
-- Faz 2'de bir relay bu tablodan okuyup Kafka'ya gönderecek.
CREATE TABLE outbox (
    id            UUID PRIMARY KEY,          -- = eventId
    aggregate_id  UUID         NOT NULL,     -- = orderId (Kafka mesaj anahtarı olacak)
    event_type    VARCHAR(100) NOT NULL,
    payload       JSONB        NOT NULL,
    created_at    TIMESTAMPTZ  NOT NULL,
    published_at  TIMESTAMPTZ
);

-- Relay yalnızca gönderilmemiş satırları tarayacağı için kısmi indeks.
CREATE INDEX ix_outbox_unpublished ON outbox (created_at) WHERE published_at IS NULL;
