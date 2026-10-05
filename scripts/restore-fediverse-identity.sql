-- restore the fediverse identity that migration 20261001094753 left NULL on the mirrored publishers
--
-- Migration 20261005074050_DropPublisherAvatarHeaderUrls folded avatar_url/header_url into the
-- picture/background file references (addressed by url), so this script restores the images into
-- whichever shape the database is on: run it before or after that migration, both are fine.
\set ON_ERROR_STOP on
BEGIN;

DO $check$
BEGIN
    IF to_regclass('public.fediverse_actors_backup') IS NULL THEN
        RAISE EXCEPTION 'fediverse_actors_backup is missing - nothing left to restore identity from';
    END IF;
END
$check$;

-- deleted actors must not come back as live publishers
DO $softdelete$
DECLARE
    soft_deleted bigint;
BEGIN
    UPDATE publishers p SET deleted_at = s.deleted_at
    FROM fediverse_actors_backup s
    WHERE p.id = s.id
      AND s.publisher_id IS NULL
      AND s.deleted_at IS NOT NULL
      AND p.deleted_at IS NULL;
    GET DIAGNOSTICS soft_deleted = ROW_COUNT;
    RAISE NOTICE 'phase 2: soft-deleted % publishers', soft_deleted;
END
$softdelete$;

-- local actors keep their real publisher row; move the references off the shadow rows
DO $remap$
DECLARE
    remapped bigint;
BEGIN
    UPDATE posts x SET publisher_id = s.publisher_id
    FROM fediverse_actors_backup s
    WHERE s.publisher_id IS NOT NULL AND x.publisher_id = s.id;
    GET DIAGNOSTICS remapped = ROW_COUNT;
    RAISE NOTICE 'phase 2: remapped % posts', remapped;

    UPDATE boosts b SET publisher_id = s.publisher_id
    FROM fediverse_actors_backup s
    WHERE s.publisher_id IS NOT NULL AND b.publisher_id = s.id;

    UPDATE post_reactions r SET publisher_id = s.publisher_id
    FROM fediverse_actors_backup s
    WHERE s.publisher_id IS NOT NULL AND r.publisher_id = s.id;

    UPDATE quote_authorizations q SET publisher_id = s.publisher_id
    FROM fediverse_actors_backup s
    WHERE s.publisher_id IS NOT NULL AND q.publisher_id = s.id;

    UPDATE fediverse_keys k SET publisher_id = s.publisher_id
    FROM fediverse_actors_backup s
    WHERE s.publisher_id IS NOT NULL AND k.publisher_id = s.id;
END
$remap$;

-- Builds the file reference payload the .NET jsonb serializer writes for an external image
-- (PascalCase, no naming policy). Mirrors SnCloudFileReferenceObject.FromExternalUrl.
CREATE FUNCTION pg_temp.publisher_image(url text) RETURNS jsonb
LANGUAGE sql IMMUTABLE AS $fn$
    SELECT jsonb_build_object(
        'Id', '', 'Name', '',
        'FileMeta', '{}'::jsonb, 'UserMeta', '{}'::jsonb,
        'SensitiveMarks', '[]'::jsonb,
        'MimeType', NULL, 'Hash', NULL, 'Size', 0,
        'HasCompression', false, 'HasThumbnail', false, 'Status', 0,
        'Url', url,
        'Width', NULL, 'Height', NULL, 'Blurhash', NULL,
        'Usage', NULL, 'ApplicationType', NULL,
        'CreatedAt', '{}'::jsonb, 'UpdatedAt', '{}'::jsonb
    );
$fn$;

-- Restores the avatar/header into the columns that exist on this database. The branch that
-- references the dropped columns is never planned on a migrated database, and vice versa.
DO $images$
DECLARE
    legacy_columns boolean;
    restored bigint;
BEGIN
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'publishers' AND column_name = 'avatar_url'
    ) INTO legacy_columns;

    IF legacy_columns THEN
        UPDATE publishers p SET
            avatar_url = s.avatar_url,
            header_url = s.header_url
        FROM fediverse_actors_backup s
        WHERE p.id = s.id
          AND s.publisher_id IS NULL
          AND s.deleted_at IS NULL
          AND p.uri IS NULL;
    ELSE
        UPDATE publishers p SET
            picture = CASE WHEN s.avatar_url IS NULL THEN p.picture
                           ELSE pg_temp.publisher_image(s.avatar_url) END,
            background = CASE WHEN s.header_url IS NULL THEN p.background
                              ELSE pg_temp.publisher_image(s.header_url) END
        FROM fediverse_actors_backup s
        WHERE p.id = s.id
          AND s.publisher_id IS NULL
          AND s.deleted_at IS NULL
          AND p.uri IS NULL
          AND (s.avatar_url IS NOT NULL OR s.header_url IS NOT NULL);
    END IF;

    GET DIAGNOSTICS restored = ROW_COUNT;
    RAISE NOTICE 'phase 2: restored images on % publisher rows', restored;
END
$images$;

DO $restore$
DECLARE
    restored bigint;
    dropped  bigint;
BEGIN
    UPDATE publishers p SET
        actor_type        = s.type,
        uri               = s.uri,
        username          = s.username,
        instance_id       = s.instance_id,
        instance_domain   = i.domain,
        inbox_uri         = s.inbox_uri,
        outbox_uri        = s.outbox_uri,
        followers_uri     = s.followers_uri,
        following_uri     = s.following_uri,
        featured_uri      = s.featured_uri,
        public_key_id     = s.public_key_id,
        public_key        = s.public_key,
        is_bot            = s.is_bot,
        is_locked         = s.is_locked,
        is_discoverable   = s.is_discoverable,
        is_community      = s.is_community,
        metadata          = s.metadata,
        last_fetched_at   = s.last_fetched_at,
        last_activity_at  = s.last_activity_at,
        outbox_fetched_at = s.outbox_fetched_at
    FROM fediverse_actors_backup s
    LEFT JOIN fediverse_instances i ON i.id = s.instance_id
    WHERE p.id = s.id
      AND s.publisher_id IS NULL
      AND s.deleted_at IS NULL
      AND p.uri IS NULL;
    GET DIAGNOSTICS restored = ROW_COUNT;
    RAISE NOTICE 'phase 2: restored fediverse identity on % publisher rows', restored;

    DELETE FROM publishers p
    USING fediverse_actors_backup s
    WHERE p.id = s.id
      AND s.publisher_id IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM posts x WHERE x.publisher_id = p.id)
      AND NOT EXISTS (SELECT 1 FROM boosts b WHERE b.publisher_id = p.id)
      AND NOT EXISTS (SELECT 1 FROM post_reactions r WHERE r.publisher_id = p.id)
      AND NOT EXISTS (SELECT 1 FROM quote_authorizations q WHERE q.publisher_id = p.id)
      AND NOT EXISTS (SELECT 1 FROM fediverse_keys k WHERE k.publisher_id = p.id);
    GET DIAGNOSTICS dropped = ROW_COUNT;
    RAISE NOTICE 'removed % shadow publisher rows', dropped;

    SELECT count(*) INTO restored
    FROM publishers
    WHERE type = 2 AND deleted_at IS NULL AND (username IS NULL OR uri IS NULL);
    RAISE NOTICE '% live mirror publishers still without identity', restored;
END
$restore$;

COMMIT;
