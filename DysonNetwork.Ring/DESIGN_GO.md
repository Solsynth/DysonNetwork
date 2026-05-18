# DysonNetwork.Ring — Go Redesign

## Overview

Go rewrite of the Ring notification service. Replaces NATS-based internal queuing with goroutine worker pools. Maintains the same database schema, API contracts, and gRPC proto definitions for backward compatibility with other services.

## Architecture

```
┌──────────────────────────────────────────────────────┐
│                      Ring Service                     │
│                                                      │
│  ┌────────────┐   ┌────────────┐   ┌──────────────┐ │
│  │  HTTP API   │   │  gRPC API  │   │  SSE Streams │ │
│  │  (Chi/Gin)  │   │ (grpc-go)  │   │  (per-user)  │ │
│  └─────┬──────┘   └─────┬──────┘   └──────┬───────┘ │
│        │                │                  │         │
│        └────────┬───────┘                  │         │
│                 ▼                          │         │
│        ┌────────────────┐                  │         │
│        │  Dispatcher    │──────────────────┘         │
│        │  (channel)     │                            │
│        └───────┬────────┘                            │
│                ▼                                     │
│  ┌─────────────────────────────┐                     │
│  │     Worker Pool (N)         │                     │
│  │  ┌─────┐ ┌─────┐ ┌─────┐  │                     │
│  │  │  W  │ │  W  │ │  W  │  │                     │
│  │  └──┬──┘ └──┬──┘ └──┬──┘  │                     │
│  └─────┼───────┼───────┼─────┘                     │
│        ▼       ▼       ▼                           │
│  ┌─────────┐ ┌─────┐ ┌────────────┐                │
│  │ APNs    │ │ FCM │ │ UnifiedPush│                │
│  └─────────┘ └─────┘ └────────────┘                │
│        │                                            │
│        ▼                                            │
│  ┌──────────────┐   ┌──────────────┐                │
│  │   PostgreSQL  │   │    Redis     │                │
│  └──────────────┘   └──────────────┘                │
└──────────────────────────────────────────────────────┘
```

## Core Change: Goroutine Worker Pool

### Before (C# / NATS)

```
gRPC/API → QueueService → NATS("pusher_queue") → QueueBackgroundService → PushService
                                                            (N consumers)
```

### After (Go / Goroutines)

```
gRPC/API → Dispatcher.Send() → channel → Worker Pool → PushService
                                    (N goroutines)
```

The dispatcher is a buffered channel. Workers pull from it and fan out to providers.

```go
type Dispatcher struct {
    queue    chan *DeliveryJob
    workers  int
    // ...
}

type DeliveryJob struct {
    Type        JobType // Email, PushNotification
    TargetID    string  // user ID (for push)
    Payload     []byte  // JSON-serialized data
    Excludes    []string // device IDs to skip (for WebSocket)
}
```

`workers` defaults to `runtime.NumCPU()`, configurable via `RING_WORKER_COUNT` env or config.

## Package Layout

```
ring/
├── cmd/
│   └── ring/
│       └── main.go              # Entry point, wiring
├── internal/
│   ├── api/
│   │   ├── router.go            # Chi router setup
│   │   ├── notifications.go     # NotificationController
│   │   └── sop.go               # SopNotificationController
│   ├── grpc/
│   │   └── ring.go              # DyRingService gRPC impl
│   ├── model/
│   │   ├── notification.go      # Notification entity
│   │   ├── subscription.go      # PushSubscription entity
│   │   ├── preference.go        # NotificationPreference entity
│   │   └── enums.go             # PushProvider, PreferenceLevel
│   ├── store/
│   │   ├── postgres.go          # pgx pool setup
│   │   ├── notifications.go     # Notification queries
│   │   ├── subscriptions.go     # PushSubscription queries
│   │   └── preferences.go       # NotificationPreference queries
│   ├── push/
│   │   ├── dispatcher.go        # Buffered channel + dispatch logic
│   │   ├── worker.go            # Worker goroutine pool
│   │   ├── provider.go          # Provider interface
│   │   ├── apple.go             # APNs sender
│   │   ├── google.go            # FCM sender
│   │   ├── unifiedpush.go       # UnifiedPush sender
│   │   ├── sop.go               # SOP in-memory broadcast
│   │   └── websocket.go         # WebSocket relay via gRPC to Messager
│   ├── email/
│   │   └── sender.go            # SMTP via net/smtp or gomail
│   ├── preference/
│   │   └── service.go           # Preference CRUD + filtering
│   ├── flush/
│   │   └── buffer.go            # Batched invalid token removal
│   └── db/
│       ├── migrate.go           # Schema migration (golang-migrate)
│       └── retention.go         # Scheduled cleanup goroutines
├── proto/
│   ├── ring.proto               # gRPC service definition
│   └── ring.pb.go               # Generated
├── config/
│   └── config.go                # Config struct + env loading
├── go.mod
├── go.sum
└── Dockerfile
```

## Dispatcher & Worker Pool

### Dispatcher

```go
type Dispatcher struct {
    jobs     chan *DeliveryJob
    store    *store.Store
    push     *push.Deliverer
    email    *email.Sender
    flush    *flush.Buffer
    logger   *slog.Logger
}

func NewDispatcher(workers int, bufferSize int) *Dispatcher {
    return &Dispatcher{
        jobs:   make(chan *DeliveryJob, bufferSize),
        // ...
    }
}

func (d *Dispatcher) Start(ctx context.Context) {
    for i := 0; i < d.workers; i++ {
        go d.worker(ctx, i)
    }
}

func (d *Dispatcher) Send(job *DeliveryJob) {
    d.jobs <- job
}

func (d *Dispatcher) worker(ctx context.Context, id int) {
    for {
        select {
        case <-ctx.Done():
            return
        case job := <-d.jobs:
            d.process(ctx, id, job)
        }
    }
}
```

### Worker Processing

```go
func (d *Dispatcher) process(ctx context.Context, id int, job *DeliveryJob) {
    switch job.Type {
    case JobEmail:
        var msg email.Message
        json.Unmarshal(job.Payload, &msg)
        if err := d.email.Send(ctx, &msg); err != nil {
            d.logger.Error("email send failed", "worker", id, "err", err)
        }

    case JobPush:
        var notif push.Notification
        json.Unmarshal(job.Payload, &notif)
        if err := d.push.Deliver(ctx, job.TargetID, &notif, job.Excludes); err != nil {
            d.logger.Error("push delivery failed", "worker", id, "target", job.TargetID, "err", err)
        }
    }
}
```

### Buffer Size

Default buffer: 10,000 jobs. Configurable via `RING_QUEUE_BUFFER`. When the buffer is full, `Send()` blocks — this is intentional backpressure. For gRPC callers, set a short timeout so they fail fast rather than hanging.

## Push Delivery

### Provider Interface

```go
type Provider interface {
    Name() string
    Send(ctx context.Context, sub *model.PushSubscription, notif *Notification) error
    IsRetryable(err error) bool
}
```

### Deliverer

```go
type Deliverer struct {
    store      *store.Store
    providers  map[model.PushProvider]Provider
    sop        *SOPBroadcaster
    ws         *WebSocketRelay
    flush      *flush.Buffer
}

func (d *Deliverer) Deliver(ctx context.Context, userID string, notif *Notification, excludes []string) error {
    // 1. Broadcast to SOP streams (in-memory)
    d.sop.Broadcast(userID, notif)

    // 2. Fetch all active subscriptions for user
    subs, err := d.store.Subscriptions.FindActiveByAccount(userID)
    if err != nil {
        return err
    }

    // 3. Select one per physical device (group by deviceID, priority: SOP > APNs > FCM > UP)
    selected := selectBestPerDevice(subs)

    // 4. Push via WebSocket (exclude SOP-connected devices)
    d.ws.Push(ctx, userID, notif, excludes)

    // 5. Fan out to platform providers concurrently
    var wg sync.WaitGroup
    for _, sub := range selected {
        if sub.Provider == model.ProviderSOP {
            continue // already delivered via SSE
        }
        wg.Add(1)
        go func(sub *model.PushSubscription) {
            defer wg.Done()
            d.deliverToProvider(ctx, sub, notif)
        }(sub)
    }
    wg.Wait()

    return nil
}
```

### Provider Fan-Out (Concurrent)

Each provider call runs in its own goroutine. Failed tokens are enqueued for batch removal.

```go
func (d *Deliverer) deliverToProvider(ctx context.Context, sub *model.PushSubscription, notif *Notification) {
    provider, ok := d.providers[sub.Provider]
    if !ok {
        return
    }
    if err := provider.Send(ctx, sub, notif); err != nil {
        if isTokenInvalid(err) {
            d.flush.Enqueue(flush.PushSubRemovalRequest{SubID: sub.ID})
        }
    }
}
```

### Device Selection Logic

Group subscriptions by normalized `deviceID` (strip `:sop` suffix). Within each group, pick one by priority:

```
Priority order:
  1. SOP + connected (has active SSE stream)
  2. APNs (Apple)
  3. FCM (Google)
  4. UnifiedPush
```

This ensures each physical device gets exactly one push per notification.

## SOP (SSE Streams)

SOP is an in-memory pub-sub per user. No database involvement for delivery.

```go
type SOPBroadcaster struct {
    mu      sync.RWMutex
    streams map[string]map[string]*Stream // userID → deviceID → stream
}

type Stream struct {
    Token    string
    DeviceID string
    Ch       chan *Notification
    Done     chan struct{}
}

func (s *SOPBroadcaster) Register(userID, deviceID, token string) *Stream {
    s.mu.Lock()
    defer s.mu.Unlock()
    ch := make(chan *Notification, 64)
    stream := &Stream{Token: token, DeviceID: deviceID, Ch: ch, Done: make(chan struct{})}
    if s.streams[userID] == nil {
        s.streams[userID] = make(map[string]*Stream)
    }
    s.streams[userID][deviceID] = stream
    return stream
}

func (s *SOPBroadcaster) Broadcast(userID string, notif *Notification) {
    s.mu.RLock()
    defer s.mu.RUnlock()
    for _, stream := range s.streams[userID] {
        select {
        case stream.Ch <- notif:
        default:
            // buffer full, drop (client should reconnect)
        }
    }
}

func (s *SOPBroadcaster) Unregister(userID, deviceID string) {
    s.mu.Lock()
    defer s.mu.Unlock()
    if devices, ok := s.streams[userID]; ok {
        if stream, ok := devices[deviceID]; ok {
            close(stream.Done)
            close(stream.Ch)
            delete(devices, deviceID)
        }
        if len(devices) == 0 {
            delete(s.streams, userID)
        }
    }
}
```

### SSE Handler

```go
func (h *SOPHandler) Stream(w http.ResponseWriter, r *http.Request) {
    token := extractSOPToken(r)
    userID, deviceID := h.validateToken(token)

    stream := h.sop.Register(userID, deviceID, token)
    defer h.sop.Unregister(userID, deviceID)

    w.Header().Set("Content-Type", "text/event-stream")
    w.Header().Set("Cache-Control", "no-cache")
    w.Header().Set("Connection", "keep-alive")
    flusher := w.(http.Flusher)

    fmt.Fprintf(w, "event: ready\ndata: {}\n\n")
    flusher.Flush()

    for {
        select {
        case <-r.Context().Done():
            return
        case <-stream.Done:
            return
        case notif := <-stream.Ch:
            data, _ := json.Marshal(notif)
            fmt.Fprintf(w, "event: notification\ndata: %s\n\n", data)
            flusher.Flush()
        }
    }
}
```

**Note:** SOP streams are process-local. If running multiple instances, each has its own stream map. This is acceptable — a user's device connects to one instance. For load balancing, use sticky sessions or consistent hashing by userID.

## Email

```go
type Sender struct {
    smtp       *smtp.Client
    fromAddr   string
    fromName   string
    subjectPrefix string
}

func (s *Sender) Send(ctx context.Context, msg *Message) error {
    // Build MIME message with net/mail + mime/multipart
    // Send via net/smtp or gomail
}
```

No templates — body is raw HTML from caller. Subject gets `[SubjectPrefix]` prepended.

## Flush Buffer (Batched Token Removal)

```go
type Buffer struct {
    mu      sync.Mutex
    queue   []PushSubRemovalRequest
    store   *store.Store
    maxSize int
}

func (b *Buffer) Enqueue(req PushSubRemovalRequest) {
    b.mu.Lock()
    defer b.mu.Unlock()
    b.queue = append(b.queue, req)
}

func (b *Buffer) Flush(ctx context.Context) {
    b.mu.Lock()
    batch := b.queue
    b.queue = nil
    b.mu.Unlock()

    ids := make([]uuid.UUID, len(batch))
    for i, r := range batch {
        ids[i] = r.SubID
    }
    b.store.Subscriptions.DeleteByIDs(ctx, ids)
}
```

### Cleanup Goroutine

Replaces Quartz `PushSubFlushJob`. Runs in a background goroutine with `time.Ticker`:

```go
func runFlushLoop(ctx context.Context, buf *flush.Buffer, interval time.Duration) {
    ticker := time.NewTicker(interval)
    defer ticker.Stop()
    for {
        select {
        case <-ctx.Done():
            return
        case <-ticker.C:
            buf.Flush(ctx)
        }
    }
}
```

## Retention Cleanup Goroutines

Replace Quartz scheduled jobs:

```go
func runRetentionCleanup(ctx context.Context, db *sql.DB) {
    ticker := time.NewTicker(24 * time.Hour)
    defer ticker.Stop()
    for {
        select {
        case <-ctx.Done():
            return
        case <-ticker.C:
            // Delete notifications older than 30 days
            db.ExecContext(ctx, `DELETE FROM notifications WHERE created_at < now() - interval '30 days'`)
            // Delete soft-deleted records older than 7 days
            db.ExecContext(ctx, `DELETE FROM notifications WHERE deleted_at IS NOT NULL AND deleted_at < now() - interval '7 days'`)
            // Same for push_subscriptions and notification_preferences
        }
    }
}
```

## Database

PostgreSQL via `pgxpool` (not ORM — raw SQL or `sqlc` for generated Go code).

### Schema

Identical to the C# version. Three tables:

- `notifications` — id, topic, title, subtitle, content, meta (jsonb), priority, viewed_at, account_id, created_at, updated_at, deleted_at
- `push_subscriptions` — id, account_id, device_id, device_token, provider, is_activated, count_delivered, last_used_at, created_at, updated_at, deleted_at
- `notification_preferences` — id, account_id, topic, preference, created_at, updated_at, deleted_at

### Migrations

Use `golang-migrate/migrate` or embed SQL migrations in the binary.

## Configuration

```go
type Config struct {
    // Server
    HTTPAddr   string `env:"RING_HTTP_ADDR"   default:":5212"`
    GRPCAddr   string `env:"RING_GRPC_ADDR"   default:":5213"`

    // Database
    DatabaseURL string `env:"RING_DATABASE_URL"`

    // Redis
    RedisURL string `env:"RING_REDIS_URL"`

    // Workers
    WorkerCount int    `env:"RING_WORKER_COUNT" default:"0"` // 0 = runtime.NumCPU()
    QueueBuffer int    `env:"RING_QUEUE_BUFFER" default:"10000"`

    // Email SMTP
    Email struct {
        Server        string `env:"EMAIL_SERVER"`
        Port          int    `env:"EMAIL_PORT" default:"587"`
        UseSSL        bool   `env:"EMAIL_USE_SSL"`
        Username      string `env:"EMAIL_USERNAME"`
        Password      string `env:"EMAIL_PASSWORD"`
        FromAddress   string `env:"EMAIL_FROM_ADDRESS"`
        FromName      string `env:"EMAIL_FROM_NAME"`
        SubjectPrefix string `env:"EMAIL_SUBJECT_PREFIX"`
    }

    // APNs
    Apple struct {
        PrivateKeyPath    string `env:"APPLE_PRIVATE_KEY_PATH"`
        PrivateKeyID      string `env:"APPLE_PRIVATE_KEY_ID"`
        TeamID            string `env:"APPLE_TEAM_ID"`
        BundleID          string `env:"APPLE_BUNDLE_ID"`
        Production        bool   `env:"APPLE_PRODUCTION"`
    }

    // FCM
    Google struct {
        ServiceAccountPath string `env:"GOOGLE_SA_PATH"`
    }

    // Flush
    FlushInterval time.Duration `env:"FLUSH_INTERVAL" default:"5m"`
    FlushMaxSize  int           `env:"FLUSH_MAX_SIZE"  default:"500"`

    // Retention
    NotificationRetentionDays int `env:"NOTIFICATION_RETENTION_DAYS" default:"30"`
    SoftDeleteRetentionDays   int `env:"SOFT_DELETE_RETENTION_DAYS"  default:"7"`
}
```

## gRPC Service

Same proto definitions. Go implementation uses `grpc-go`.

```go
type RingServer struct {
    pb.UnimplementedDyRingServiceServer
    dispatcher *Dispatcher
    store      *store.Store
}

func (s *RingServer) SendEmail(ctx context.Context, req *pb.DySendEmailRequest) (*emptypb.Empty, error) {
    data, _ := json.Marshal(req.Email)
    s.dispatcher.Send(&DeliveryJob{Type: JobEmail, Payload: data})
    return &emptypb.Empty{}, nil
}

func (s *RingServer) SendPushNotificationToUser(ctx context.Context, req *pb.DySendPushNotificationToUserRequest) (*emptypb.Empty, error) {
    // Save to DB if is_savable
    // Enqueue via dispatcher
    return &emptypb.Empty{}, nil
}

func (s *RingServer) SendPushNotificationToUsers(ctx context.Context, req *pb.DySendPushNotificationToUsersRequest) (*emptypb.Empty, error) {
    for _, uid := range req.UserIds {
        // Save + enqueue per user
    }
    return &emptypb.Empty{}, nil
}

func (s *RingServer) UnsubscribePushNotifications(ctx context.Context, req *pb.DyUnsubscribePushNotificationsRequest) (*emptypb.Empty, error) {
    s.store.Subscriptions.DeleteByDeviceID(ctx, req.DeviceId)
    return &emptypb.Empty{}, nil
}
```

## Startup Sequence

```go
func main() {
    ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
    defer cancel()

    cfg := config.Load()

    // Database
    db := store.NewPool(ctx, cfg.DatabaseURL)
    defer db.Close()

    // Redis (optional, for cache)
    // rdb := redis.NewClient(...)

    // Flush buffer
    flushBuf := flush.NewBuffer(db, cfg.FlushMaxSize)

    // SOP broadcaster (in-memory)
    sop := push.NewSOPBroadcaster()

    // WebSocket relay (gRPC client to Messager)
    wsRelay := push.NewWebSocketRelay(cfg.MessagerGRPCAddr)

    // Providers
    providers := map[model.PushProvider]push.Provider{
        model.ProviderApple:     apple.NewSender(cfg.Apple),
        model.ProviderGoogle:    google.NewSender(cfg.Google),
        model.ProviderUnifiedPush: unifiedpush.NewSender(),
    }

    // Deliverer
    deliverer := push.NewDeliverer(db, providers, sop, wsRelay, flushBuf)

    // Email
    emailSender := email.NewSender(cfg.Email)

    // Dispatcher
    dispatcher := push.NewDispatcher(deliverer, emailSender, flushBuf, cfg.WorkerCount, cfg.QueueBuffer)
    dispatcher.Start(ctx)
    defer dispatcher.Stop()

    // Background goroutines
    go runFlushLoop(ctx, flushBuf, cfg.FlushInterval)
    go runRetentionCleanup(ctx, db, cfg)

    // HTTP API
    router := api.NewRouter(db, dispatcher, deliverer, sop, ...)
    httpServer := &http.Server{Addr: cfg.HTTPAddr, Handler: router}

    // gRPC server
    grpcServer := grpc.NewServer()
    pb.RegisterDyRingServiceServer(grpcServer, &RingServer{dispatcher: dispatcher, store: db})

    // Run
    go httpServer.ListenAndServe()
    go grpcServer.ListenAndServe(cfg.GRPCAddr)

    <-ctx.Done()
    httpServer.Shutdown(context.Background())
    grpcServer.GracefulStop()
}
```

## Migration Path

| C# Component | Go Equivalent |
|--------------|---------------|
| NATS QueueService | `Dispatcher` (buffered channel) |
| QueueBackgroundService | `Dispatcher.worker()` goroutines |
| PushService | `push.Deliverer` |
| EmailService | `email.Sender` |
| FlushBufferService | `flush.Buffer` |
| NotificationPreferenceService | `preference.Service` |
| PushSubFlushJob (Quartz) | `runFlushLoop` goroutine |
| NotificationRetentionCleanupJob | `runRetentionCleanup` goroutine |
| AppDatabaseRecyclingJob | `runRetentionCleanup` goroutine |
| RemoteWebSocketService | `push.WebSocketRelay` (gRPC client) |
| SOP streams (ConcurrentDictionary) | `SOPBroadcaster` (sync.RWMutex + map) |
| CorePush (APNs/FCM) | `go-apns` / `go-fcm` or raw HTTP/2 |
| MailKit (SMTP) | `net/smtp` or `gomail.v2` |

## Trade-offs

| Aspect | NATS (Before) | Goroutines (After) |
|--------|---------------|-------------------|
| Horizontal scaling | ✅ Multiple consumers across nodes | ❌ Single-process only |
| Simplicity | ❌ Requires NATS infra | ✅ No external dependency |
| Backpressure | ✅ NATS handles queuing | ⚠️ Channel buffer + blocking |
| Observability | ✅ NATS dashboard | ⚠️ Need custom metrics |
| Throughput | Higher (distributed) | Lower (single node, but sufficient for most cases) |
| Deployment | More complex | Simpler (single binary) |

**If horizontal scaling is needed later**, replace the channel with a Redis Stream or NATS JetStream and implement the same `Dispatcher` interface — the rest of the code stays the same.
