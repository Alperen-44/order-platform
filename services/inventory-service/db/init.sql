-- Inventory servisinin şeması. Container ilk kez oluşturulurken çalışır.

CREATE TABLE stock (
    product_id  UUID PRIMARY KEY,
    sku         VARCHAR(64)  NOT NULL UNIQUE,
    name        VARCHAR(200) NOT NULL,
    available   INT          NOT NULL CHECK (available >= 0),  -- son savunma hattı: stok asla eksiye düşemez
    reserved    INT          NOT NULL CHECK (reserved >= 0),
    updated_at  TIMESTAMPTZ  NOT NULL
);

CREATE TABLE stock_reservations (
    id          UUID PRIMARY KEY,
    order_id    UUID        NOT NULL,
    product_id  UUID        NOT NULL REFERENCES stock (product_id),
    quantity    INT         NOT NULL CHECK (quantity > 0),
    status      VARCHAR(32) NOT NULL,   -- Reserved | Released | Committed
    created_at  TIMESTAMPTZ NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL,
    -- Aynı sipariş için aynı ürün iki kez rezerve edilemez (idempotency'nin veritabanı garantisi)
    CONSTRAINT uq_reservation_order_product UNIQUE (order_id, product_id)
);

CREATE INDEX ix_reservations_order_id ON stock_reservations (order_id);

-- Faz 2: Inventory de olay yayınlıyor (StockReserved / StockReservationFailed).
-- Order servisindeki outbox ile aynı şema; ortak OutboxRelay ikisini de okuyabiliyor.
CREATE TABLE outbox (
    id            UUID PRIMARY KEY,
    aggregate_id  UUID         NOT NULL,
    event_type    VARCHAR(100) NOT NULL,
    payload       JSONB        NOT NULL,
    created_at    TIMESTAMPTZ  NOT NULL,
    published_at  TIMESTAMPTZ
);

CREATE INDEX ix_outbox_unpublished ON outbox (created_at) WHERE published_at IS NULL;

-- Faz 3: Idempotent consumer. Bu servisin Kafka'dan işlediği olaylar.
-- İş mantığının değişikliğiyle AYNI transaction'da yazılır; aynı eventId tekrar gelirse atlanır.
CREATE TABLE processed_events (
    consumer_group  VARCHAR(100) NOT NULL,
    event_id        UUID         NOT NULL,
    event_type      VARCHAR(100) NOT NULL,
    processed_at    TIMESTAMPTZ  NOT NULL,
    PRIMARY KEY (consumer_group, event_id)
);

-- Deneme verisi. Sabit ID'ler sayesinde scriptlerde ve Swagger'da kolayca kullanılır.
INSERT INTO stock (product_id, sku, name, available, reserved, updated_at) VALUES
    ('11111111-1111-1111-1111-111111111111', 'KB-MECH-01',  'Mekanik Klavye',              50,  0, now()),
    ('22222222-2222-2222-2222-222222222222', 'MS-WL-01',    'Kablosuz Mouse',             100,  0, now()),
    ('33333333-3333-3333-3333-333333333333', 'HP-LTD-01',   'Sınırlı Sayıda Kulaklık',      1,  0, now());
