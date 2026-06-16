-- 1. Check what constraints exist on payment_transactions
SELECT 
    conname AS constraint_name,
    contype AS constraint_type,
    pg_get_constraintdef(oid) AS constraint_definition
FROM pg_constraint 
WHERE conrelid = 'payment_transactions'::regclass;

-- 2. Check if the id column exists and its current state
SELECT 
    column_name, 
    data_type, 
    is_nullable
FROM information_schema.columns 
WHERE table_name = 'payment_transactions' 
AND column_name = 'id';

-- 3. Add primary key if missing (uncomment to run)
-- ALTER TABLE payment_transactions ADD PRIMARY KEY (id);

-- 4. If the above fails because of duplicates, find them first:
-- SELECT id, COUNT(*) 
-- FROM payment_transactions 
-- GROUP BY id 
-- HAVING COUNT(*) > 1;

-- 5. After fixing, the original migration should work. 
-- Or you can manually create the transfer requests table:
CREATE TABLE IF NOT EXISTS wallet_transfer_requests (
    id uuid NOT NULL,
    status integer NOT NULL,
    currency character varying(128) NOT NULL,
    amount numeric NOT NULL,
    remark character varying(4096),
    "freeze" boolean NOT NULL,
    require_confirmation boolean NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    fulfilled_at timestamp with time zone,
    creator_account_id uuid NOT NULL,
    payee_wallet_id uuid NOT NULL,
    transaction_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT pk_wallet_transfer_requests PRIMARY KEY (id),
    CONSTRAINT fk_wallet_transfer_requests_payment_transactions_transaction_id 
        FOREIGN KEY (transaction_id) REFERENCES payment_transactions (id),
    CONSTRAINT fk_wallet_transfer_requests_wallets_payee_wallet_id 
        FOREIGN KEY (payee_wallet_id) REFERENCES wallets (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_wallet_transfer_requests_payee_wallet_id 
    ON wallet_transfer_requests (payee_wallet_id);

CREATE INDEX IF NOT EXISTS ix_wallet_transfer_requests_transaction_id 
    ON wallet_transfer_requests (transaction_id);

-- 6. Mark the migration as applied so EF Core doesn't try again
-- INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") 
-- VALUES ('20260616114038_AddTransferRequest', '10.0.0');
