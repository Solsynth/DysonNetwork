#!/bin/bash
# Quick validation tests for ActivityPub implementation

set -e

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

SOLAR_URL="http://solar.local:5000"
MASTODON_URL="http://mastodon.local:3001"
TEST_USER="solaruser"

echo -e "${BLUE}================================"
echo "ActivityPub Validation Tests"
echo "================================${NC}"
echo ""

# Test 1: WebFinger
echo -e "${YELLOW}[1/7] Testing WebFinger...${NC}"
WEBFINGER_RESPONSE=$(curl -s "http://solar.local:5000/.well-known/webfinger?resource=acct:$TEST_USER@solar.local")

if echo "$WEBFINGER_RESPONSE" | grep -q "subject"; then
    echo -e "${GREEN}✓ WebFinger is working${NC}"
    echo "$WEBFINGER_RESPONSE" | jq '.' 2>/dev/null || echo "$WEBFINGER_RESPONSE" | head -20
else
    echo -e "${RED}✗ WebFinger failed${NC}"
    echo "Response: $WEBFINGER_RESPONSE"
fi
echo ""

# Test 2: Actor Profile
echo -e "${YELLOW}[2/7] Testing Actor Profile...${NC}"
ACTOR_RESPONSE=$(curl -s -H "Accept: application/activity+json" "http://solar.local:5000/activitypub/actors/$TEST_USER")

if echo "$ACTOR_RESPONSE" | grep -q "inbox" && echo "$ACTOR_RESPONSE" | grep -q "outbox"; then
    echo -e "${GREEN}✓ Actor profile is valid${NC}"
    echo "$ACTOR_RESPONSE" | jq '.' 2>/dev/null || echo "$ACTOR_RESPONSE" | head -30
else
    echo -e "${RED}✗ Actor profile is invalid${NC}"
    echo "Response: $ACTOR_RESPONSE"
fi
echo ""

# Test 3: Outbox
echo -e "${YELLOW}[3/7] Testing Outbox...${NC}"
OUTBOX_RESPONSE=$(curl -s -H "Accept: application/activity+json" "http://solar.local:5000/activitypub/actors/$TEST_USER/outbox")

if echo "$OUTBOX_RESPONSE" | grep -q "OrderedCollection"; then
    TOTAL_ITEMS=$(echo "$OUTBOX_RESPONSE" | jq '.totalItems' 2>/dev/null || echo "N/A")
    echo -e "${GREEN}✓ Outbox is working ($TOTAL_ITEMS items)${NC}"
else
    echo -e "${RED}✗ Outbox failed${NC}"
    echo "Response: $OUTBOX_RESPONSE"
fi
echo ""

# Test 4: Public Key Presence
echo -e "${YELLOW}[4/7] Checking Public Key in Actor...${NC}"
PUBLIC_KEY=$(echo "$ACTOR_RESPONSE" | jq -r '.publicKey.publicKeyPem' 2>/dev/null)

if [ -n "$PUBLIC_KEY" ] && [ "$PUBLIC_KEY" != "null" ]; then
    KEY_TYPE=$(echo "$PUBLIC_KEY" | head -1)
    echo -e "${GREEN}✓ Public key present ($KEY_TYPE)${NC}"
else
    echo -e "${RED}✗ No public key found${NC}"
fi
echo ""

# Test 5: Database - Actors
echo -e "${YELLOW}[5/7] Checking Database - Actors...${NC}"
ACTORS_COUNT=$(psql -d dyson_network -t -c "SELECT COUNT(*) FROM fediverse_actors;" 2>/dev/null || echo "0")

if [ "$ACTORS_COUNT" -gt 0 ]; then
    echo -e "${GREEN}✓ Found $ACTORS_COUNT actors in database${NC}"
    psql -d dyson_network -c "SELECT uri, username FROM fediverse_actors LIMIT 5;" 2>/dev/null || true
else
    echo -e "${YELLOW}No actors in database yet${NC}"
fi
echo ""

# Test 6: Database - Activities
echo -e "${YELLOW}[6/7] Checking Database - Activities...${NC}"
ACTIVITIES_COUNT=$(psql -d dyson_network -t -c "SELECT COUNT(*) FROM fediverse_activities;" 2>/dev/null || echo "0")

if [ "$ACTIVITIES_COUNT" -gt 0 ]; then
    echo -e "${GREEN}✓ Found $ACTIVITIES_COUNT activities in database${NC}"
    echo ""
    echo "Recent activities:"
    psql -d dyson_network -c "SELECT type, status, created_at FROM fediverse_activities ORDER BY created_at DESC LIMIT 5;" 2>/dev/null || true
else
    echo -e "${YELLOW}No activities in database yet (expected before federation tests)${NC}"
fi
echo ""

# Test 7: Publisher Keys
echo -e "${YELLOW}[7/7] Checking Publisher Keys...${NC}"
KEYS_COUNT=$(psql -d dyson_network -t -c "SELECT COUNT(*) FROM publishers WHERE meta IS NOT NULL;" 2>/dev/null || echo "0")

if [ "$KEYS_COUNT" -gt 0 ]; then
    echo -e "${GREEN}✓ Found $KEYS_COUNT publishers with keys${NC}"
    psql -d dyson_network -c "SELECT id, name FROM publishers WHERE meta IS NOT NULL LIMIT 5;" 2>/dev/null || true
else
    echo -e "${YELLOW}No publishers with keys yet (keys will be generated on first federation activity)${NC}"
fi
echo ""

# Summary
echo -e "${BLUE}================================"
echo "Test Summary"
echo "================================${NC}"
echo ""
echo "Solar Network: $SOLAR_URL"
echo "Mastodon:     $MASTODON_URL"
echo ""
echo "All basic validation tests completed!"
echo ""
echo "Next steps:"
echo "  1. Test federation between Solar Network and Mastodon"
echo "  2. Create a post in Solar Network"
echo "  3. Verify it appears in Mastodon"
echo "  4. Follow users across instances"
echo "  5. Test likes and replies"
echo ""
echo "For detailed testing scenarios, see: ACTIVITYPUB_TESTING_GUIDE.md"
echo "For quick command reference, see: ACTIVITYPUB_TESTING_QUICKREF.md"
echo ""

# Health checks
echo -e "${BLUE}Health Status:${NC}"

# Check Solar Network
if curl -s -f "http://solar.local:5000" > /dev/null; then
    echo -e "  ${GREEN}✓ Solar Network is accessible${NC}"
else
    echo -e "  ${RED}✗ Solar Network is not accessible${NC}"
fi

# Check Mastodon
if curl -s -f "http://mastodon.local:3001" > /dev/null; then
    echo -e "  ${GREEN}✓ Mastodon is accessible${NC}"
else
    echo -e "  ${RED}✗ Mastodon is not accessible${NC}"
fi

# Check PostgreSQL
if psql -d dyson_network -c "SELECT 1;" > /dev/null 2>&1; then
    echo -e "  ${GREEN}✓ Database is accessible${NC}"
else
    echo -e "  ${RED}✗ Database is not accessible${NC}"
fi

echo ""
