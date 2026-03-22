-- Test query for nearby meets
-- Replace these values with your test data:
-- @user_id: the UUID of the user making the query
-- @user_location: the WKT of user's current location (e.g., 'POINT(113.889041 22.564237)')
-- @distance_meters: search radius (default 5000 for 5km)

WITH user_friends AS (
    -- Get all friends of the querying user
    SELECT related_id as friend_id
    FROM account_relationships
    WHERE account_id = @user_id::uuid
      AND status = 100  -- 100 = Friends
)
SELECT DISTINCT
    m.id,
    m.host_id,
    m.visibility,
    m.status,
    m.location_name,
    ST_AsText(m.location) as location_wkt,
    ST_Distance(
        m.location::geography,
        ST_GeomFromText(@user_location, 4326)::geography
    ) as distance_meters
FROM meets m
LEFT JOIN meet_participants mp ON m.id = mp.meet_id
LEFT JOIN user_friends uf_host ON uf_host.friend_id = m.host_id
LEFT JOIN user_friends uf_participant ON uf_participant.friend_id = mp.account_id
WHERE m.location IS NOT NULL
  AND m.status = 0  -- 0 = Active
  -- Distance filter (5km default for Public meets)
  AND ST_DWithin(
    m.location::geography,
    ST_GeomFromText(@user_location, 4326)::geography,
    @distance_meters::double precision
  )
  -- Visibility filters
  AND (
    -- Public meets: visible to anyone within range
    m.visibility = 0
    -- User's own meets (host)
    OR m.host_id = @user_id::uuid
    -- User is a participant
    OR mp.account_id = @user_id::uuid
    -- Unlisted meets: user is friend of host or any participant
    OR (
      m.visibility = 2  -- 2 = Unlisted
      AND (uf_host.friend_id IS NOT NULL OR uf_participant.friend_id IS NOT NULL)
    )
  )
ORDER BY ST_Distance(
    m.location::geography,
    ST_GeomFromText(@user_location, 4326)::geography
)
LIMIT 50;

-- Example with actual values (uncomment and modify):
/*
WITH user_friends AS (
    SELECT related_id as friend_id
    FROM account_relationships
    WHERE account_id = 'your-user-uuid-here'::uuid
      AND status = 100
)
SELECT DISTINCT
    m.id,
    m.host_id,
    m.visibility,
    m.status,
    m.location_name,
    ST_AsText(m.location) as location_wkt,
    ST_Distance(
        m.location::geography,
        ST_GeomFromText('POINT(113.889041 22.564237)', 4326)::geography
    ) as distance_meters
FROM meets m
LEFT JOIN meet_participants mp ON m.id = mp.meet_id
LEFT JOIN user_friends uf_host ON uf_host.friend_id = m.host_id
LEFT JOIN user_friends uf_participant ON uf_participant.friend_id = mp.account_id
WHERE m.location IS NOT NULL
  AND m.status = 0
  AND ST_DWithin(
    m.location::geography,
    ST_GeomFromText('POINT(113.889041 22.564237)', 4326)::geography,
    5000
  )
  AND (
    m.visibility = 0
    OR m.host_id = 'your-user-uuid-here'::uuid
    OR mp.account_id = 'your-user-uuid-here'::uuid
    OR (
      m.visibility = 2
      AND (uf_host.friend_id IS NOT NULL OR uf_participant.friend_id IS NOT NULL)
    )
  )
ORDER BY distance_meters
LIMIT 50;
*/

-- Quick test: Find the meet at POINT(113.887454 22.565896)
/*
SELECT 
    id,
    host_id,
    visibility,
    status,
    location_name,
    ST_AsText(location) as location_wkt
FROM meets
WHERE ST_AsText(location) LIKE '%113.887454%22.565896%'
   OR ST_DWithin(
       location::geography,
       ST_GeomFromText('POINT(113.887454 22.565896)', 4326)::geography,
       10  -- within 10 meters
     );
*/
