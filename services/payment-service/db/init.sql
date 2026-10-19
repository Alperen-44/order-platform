-- Payment servisinin şeması. Container ilk kez oluşturulurken çalışır.

CREATE TABLE payments (
    id                  UUID PRIMARY KEY,
    order_id            UUID           NOT NULL UNIQUE,   -- bir siparişe en fazla bir ödeme
    customer_id         UUID           NOT NULL,
    amount              NUMERIC(12, 2) NOT NULL CHECK (amount >= 0),
    status              VARCHAR(32)    NOT NULL,          -- Completed | Failed | Refunded
    provider_reference  VARCHAR(64),
    failure_reason      VARCHAR(500),
    created_at          TIMESTAMPTZ    NOT NULL,
    updated_at          TIMESTAMPTZ    NOT NULL
);

-- PaymentCompleted / PaymentFailed / PaymentRefunded olayları (outbox pattern)
CREATE TABLE outbox (
    id            UUID PRIMARY KEY,
    aggregate_id  UUID         NOT NULL,
    event_type    VARCHAR(100) NOT NULL,
    payload       JSONB        NOT NULL,
    created_at    TIMESTAMPTZ  NOT NULL,
    published_at  TIMESTAMPTZ
);

CREATE INDEX ix_outbox_unpublished ON outbox (created_at) WHERE published_at IS NULL;

-- Idempotent consumer kaydı
CREATE TABLE processed_events (
    consumer_group  VARCHAR(100) NOT NULL,
    event_id        UUID         NOT NULL,
    event_type      VARCHAR(100) NOT NULL,
    processed_at    TIMESTAMPTZ  NOT NULL,
    PRIMARY KEY (consumer_group, event_id)
);
