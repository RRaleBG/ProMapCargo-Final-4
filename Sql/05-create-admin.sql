DO $$
DECLARE
    admin_id UUID := gen_random_uuid();
    role_id UUID;
BEGIN
    -- Kreiraj admin korisnika
    INSERT INTO "AspNetUsers" (
        id, user_name, email, normalized_email, normalized_user_name,
        password_hash, security_stamp, concurrency_stamp, email_confirmed,
        phone_number_confirmed, two_factor_enabled, lockout_enabled, lockout_end,
        access_failed_count, display_name, is_active, created_at
    ) VALUES (
        admin_id, 'admin', 'admin@local', 'ADMIN@LOCAL', 'ADMIN',
        '$2a$11$sBXoD8KgqR2KxMnXEpzc6O5fq.2YUJDmKYqvHSHCJW9w9Kh0K6YiW',
        'security-stamp-001', 'concurrency-stamp-001', true,
        false, false, false, NULL, 0, 'Administrator', true, NOW()
    );

    -- Provjeri ili kreiraj Administrator rolu
    SELECT id INTO role_id FROM "AspNetRoles" WHERE name = 'Administrator' LIMIT 1;
    IF role_id IS NULL THEN
        role_id := gen_random_uuid();
        INSERT INTO "AspNetRoles" (id, name, normalized_name)
        VALUES (role_id, 'Administrator', 'ADMINISTRATOR');
    END IF;

    -- Dodaj admin u rolu
    INSERT INTO "AspNetUserRoles" (user_id, role_id)
    VALUES (admin_id, role_id)
    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Admin kreiran: %', admin_id;
END $$;

SELECT email, user_name FROM "AspNetUsers" WHERE email = 'admin@local';
