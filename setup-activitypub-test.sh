#!/bin/bash
# Quick setup script for ActivityPub testing

set -e

echo "======================================"
echo "ActivityPub Testing Setup"
echo "======================================"
echo ""

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Configuration
SOLAR_DOMAIN="solar.local"
SOLAR_PORT="5000"
MASTODON_DOMAIN="mastodon.local"
MASTODON_PORT="3001"

echo -e "${YELLOW}1. Checking prerequisites...${NC}"

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo -e "${RED}Error: Docker is not installed${NC}"
    exit 1
fi

# Check if docker-compose is installed
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo -e "${RED}Error: docker-compose is not installed${NC}"
    exit 1
fi

# Check if psql is installed
if ! command -v psql &> /dev/null; then
    echo -e "${RED}Error: PostgreSQL client (psql) is not installed${NC}"
    exit 1
fi

echo -e "${GREEN}✓ All prerequisites found${NC}"
echo ""

echo -e "${YELLOW}2. Updating /etc/hosts...${NC}"

# Backup hosts file
sudo cp /etc/hosts /etc/hosts.backup

# Check if entries already exist
if ! grep -q "$SOLAR_DOMAIN" /etc/hosts; then
    echo "127.0.0.1 $SOLAR_DOMAIN" | sudo tee -a /etc/hosts > /dev/null
    echo -e "${GREEN}✓ Added $SOLAR_DOMAIN to hosts${NC}"
else
    echo -e "${YELLOW}⊙ $SOLAR_DOMAIN already in hosts${NC}"
fi

if ! grep -q "$MASTODON_DOMAIN" /etc/hosts; then
    echo "127.0.0.1 $MASTODON_DOMAIN" | sudo tee -a /etc/hosts > /dev/null
    echo -e "${GREEN}✓ Added $MASTODON_DOMAIN to hosts${NC}"
else
    echo -e "${YELLOW}⊙ $MASTODON_DOMAIN already in hosts${NC}"
fi

echo ""

echo -e "${YELLOW}3. Generating secrets for Mastodon...${NC}"

SECRET_KEY_BASE=$(openssl rand -base64 32 | tr -d "=+/" | cut -c1-32)
OTP_SECRET=$(openssl rand -base64 32 | tr -d "=+/" | cut -c1-32)

cat > .env.mastodon <<EOF
# Federation
LOCAL_DOMAIN=$MASTODON_DOMAIN
LOCAL_HTTPS=false

# Database
DB_HOST=db
DB_PORT=5432
DB_USER=mastodon
DB_NAME=mastodon
DB_PASS=mastodon_password

# Redis
REDIS_HOST=redis
REDIS_PORT=6379

# Elasticsearch
ES_ENABLED=true
ES_HOST=es
ES_PORT=9200

# Secrets
SECRET_KEY_BASE=$SECRET_KEY_BASE
OTP_SECRET=$OTP_SECRET

# Defaults
SINGLE_USER_MODE=false
DEFAULT_LOCALE=en
EOF

echo -e "${GREEN}✓ Generated .env.mastodon${NC}"
echo ""

echo -e "${YELLOW}4. Creating Docker Compose for Mastodon...${NC}"

cat > docker-compose.mastodon-test.yml <<EOF
version: '3'

services:
  db:
    restart: always
    image: postgres:14-alpine
    shm_size: 256mb
    networks:
      - mastodon_network
    environment:
      POSTGRES_USER: mastodon
      POSTGRES_PASSWORD: mastodon_password
      POSTGRES_DB: mastodon
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "mastodon"]
      interval: 5s
      retries: 5

  redis:
    restart: always
    image: redis:7-alpine
    networks:
      - mastodon_network
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      retries: 5

  es:
    restart: always
    image: docker.elastic.co/elasticsearch:8.10.2
    environment:
      - "discovery.type=single-node"
      - "ES_JAVA_OPTS=-Xms512m -Xmx512m"
      - "xpack.security.enabled=false"
    networks:
      - mastodon_network
    healthcheck:
      test: ["CMD-SHELL", "curl -silent http://localhost:9200/_cluster/health || exit 1"]
      interval: 10s
      retries: 10

  web:
    restart: always
    image: tootsuite/mastodon:latest
    env_file: .env.mastodon
    command: bash -c "rm -f /mastodon/tmp/pids/server.pid; bundle exec rails s -p 3000"
    ports:
      - "$MASTODON_PORT:3000"
    depends_on:
      - db
      - redis
      - es
    networks:
      - mastodon_network
    volumes:
      - ./mastodon-data/public:/mastodon/public/system
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:3000/health || exit 1"]
      interval: 10s
      retries: 5

  streaming:
    restart: always
    image: tootsuite/mastodon:latest
    env_file: .env.mastodon
    command: node ./streaming
    ports:
      - "4000:4000"
    depends_on:
      - db
      - redis
    networks:
      - mastodon_network
    healthcheck:
      test: ["CMD-SHELL", "fuser -s 4000/tcp || exit 1"]
      interval: 10s
      retries: 5

  sidekiq:
    restart: always
    image: tootsuite/mastodon:latest
    env_file: .env.mastodon
    command: bundle exec sidekiq
    depends_on:
      - db
      - redis
    networks:
      - mastodon_network
    healthcheck:
      test: ["CMD-SHELL", "ps aux | grep '[s]idekiq' || exit 1"]
      interval: 10s
      retries: 5

networks:
  mastodon_network:
    driver: bridge
EOF

echo -e "${GREEN}✓ Created docker-compose.mastodon-test.yml${NC}"
echo ""

echo -e "${YELLOW}5. Starting Mastodon...${NC}"

docker compose -f docker-compose.mastodon-test.yml up -d

echo -e "${GREEN}✓ Mastodon containers started${NC}"
echo ""
echo "Waiting for Mastodon to be ready (this may take 2-5 minutes)..."

# Wait for web service to be healthy
MAX_WAIT=300
WAIT_TIME=0
while [ $WAIT_TIME -lt $MAX_WAIT ]; do
    if docker compose -f docker-compose.mastodon-test.yml ps web | grep -q "healthy"; then
        echo -e "${GREEN}✓ Mastodon is ready!${NC}"
        break
    fi
    echo -n "."
    sleep 5
    WAIT_TIME=$((WAIT_TIME + 5))
done

echo ""

if [ $WAIT_TIME -ge $MAX_WAIT ]; then
    echo -e "${RED}✗ Mastodon failed to start within expected time${NC}"
    echo "Check logs with: docker compose -f docker-compose.mastodon-test.yml logs"
fi

echo ""
echo -e "${YELLOW}6. Creating Mastodon admin account...${NC}"

docker compose -f docker-compose.mastodon-test.yml exec web \
  bin/tootctl accounts create \
  testuser \
  testuser@$MASTODON_DOMAIN \
  --email=test@example.com \
  --confirmed \
  --role=admin \
  --approve || true

echo -e "${GREEN}✓ Created test user: testuser@$MASTODON_DOMAIN${NC}"
echo "   Password: TestPassword123!"
echo ""

echo -e "${YELLOW}7. Applying Solar Network migrations...${NC}"

cd DysonNetwork.Sphere

dotnet ef database update

echo -e "${GREEN}✓ Migrations applied${NC}"
echo ""

echo ""
echo "======================================"
echo -e "${GREEN}Setup Complete!${NC}"
echo "======================================"
echo ""
echo "Test Instances:"
echo "  Solar Network: http://$SOLAR_DOMAIN:$SOLAR_PORT"
echo "  Mastodon:     http://$MASTODON_DOMAIN:$MASTODON_PORT"
echo ""
echo "Test Accounts:"
echo "  Solar Network: Create one in the UI or via API"
echo "  Mastodon:      testuser@$MASTODON_DOMAIN (Password: TestPassword123!)"
echo ""
echo "Next Steps:"
echo "  1. Start Solar Network: dotnet run --project DysonNetwork.Sphere"
echo "  2. Create a publisher in Solar Network"
echo "  3. Open Mastodon and search for @username@solar.local"
echo "  4. Test following and posting"
echo ""
echo "Testing Commands:"
echo "  # Test WebFinger"
echo "  curl \"http://$SOLAR_DOMAIN:$SOLAR_PORT/.well-known/webfinger?resource=acct:username@$SOLAR_DOMAIN\""
echo ""
echo "  # Test Actor"
echo "  curl -H \"Accept: application/activity+json\" \\"
echo "       http://$SOLAR_DOMAIN:$SOLAR_PORT/activitypub/actors/username"
echo ""
echo "  # Test Outbox"
echo "  curl -H \"Accept: application/activity+json\" \\"
echo "       http://$SOLAR_DOMAIN:$SOLAR_PORT/activitypub/actors/username/outbox"
echo ""
echo "  # View logs"
echo "  docker compose -f docker-compose.mastodon-test.yml logs -f"
echo ""
echo "  # Stop Mastodon"
echo "  docker compose -f docker-compose.mastodon-test.yml down"
echo ""
echo "For detailed testing guide, see: ACTIVITYPUB_TESTING_GUIDE.md"
echo "For quick reference, see: ACTIVITYPUB_TESTING_QUICKREF.md"
echo ""
