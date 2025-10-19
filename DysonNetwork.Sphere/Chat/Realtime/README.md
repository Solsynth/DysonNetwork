# WebRTC Signaling Server - Client Implementation Guide

This document explains how clients should implement WebRTC signaling to work with the DysonNetwork WebRTC server.

## Overview

The WebRTC signaling server provides a WebSocket-based signaling channel for WebRTC peer-to-peer communication within chat rooms. It handles authentication, room membership verification, and message broadcasting between clients in the same chat room.

When using with the Gateway, the `/api` should be replaced with `<gateway>/sphere`

## Architecture

- **Signaling Endpoint**: `GET /api/chat/realtime/{chatId}`
- **Authentication**: JWT-based (handled by existing middleware)
- **Message Format**: WebSocketPacket (structured JSON packets)
- **Protocol**: Room-based broadcasting with client management and enforced sender validation

## Client Implementation

### 1. Prerequisites

Before implementing WebRTC signaling, ensure your client:

1. **Has Valid Authentication**: Must provide a valid JWT token for the authenticated user
2. **Is a Chat Room Member**: User must be an active member of the specified chat room
3. **Supports WebSockets**: Must be capable of establishing WebSocket connections

### 2. Connection Establishment

#### 2.1 WebSocket Connection URL

```
ws://your-server.com/api/chat/realtime/{chatId}
```

- **Protocol**: `ws://` (or `wss://` for secure connections)
- **Path**: `/api/chat/realtime/{chatId}` where `{chatId}` is the chat room GUID
- **Authentication**: Handled via existing JWT middleware (no additional query parameters needed)

#### 2.2 Authentication

The authentication is handled automatically by the server's middleware that:

1. Checks for valid JWT token in the request
2. Extracts the authenticated user (`Account`) from `HttpContext.Items["CurrentUser"]`
3. Validates that the user is a member of the specified chat room
4. Returns `401 Unauthorized` if not authenticated or `403 Forbidden` if not a room member

#### 2.3 Connection Example (JavaScript)

```javascript
class SignalingClient {
    constructor(chatId, serverUrl = 'ws://localhost:5000', userId, userName) {
        this.chatId = chatId;
        this.ws = null;
        this.serverUrl = serverUrl;
        this.isConnected = false;
        this.userId = userId;          // Current user ID
        this.userName = userName;      // Current user name
        this.onMessageHandlers = [];
    }

    // Connect to the signaling server
    async connect() {
        const url = `${this.serverUrl}/api/chat/realtime/${this.chatId}`;

        try {
            this.ws = new WebSocket(url);
            this.ws.onopen = (event) => {
                this.isConnected = true;
                console.log('Connected to signaling server for chat:', this.chatId);
            };

            this.ws.onmessage = (event) => {
                this.handleMessage(event.data);
            };

            this.ws.onclose = (event) => {
                this.isConnected = false;
                console.log('Disconnected from signaling server');
            };

            this.ws.onerror = (error) => {
                console.error('WebSocket error:', error);
            };

        } catch (error) {
            console.error('Failed to connect to signaling server:', error);
            throw error;
        }
    }

    // Disconnect from the signaling server
    disconnect() {
        if (this.ws && this.isConnected) {
            this.ws.close();
            this.isConnected = false;
        }
    }
}
```

### 3. Message Handling

#### 3.1 Enforced Message Format

The signaling server broadcasts messages using the WebSocketPacket format. All messages are automatically wrapped by the server with validated sender information. Clients should send only the signaling type and data, and receive complete packets with sender details.

**WebSocketPacket Format:**

For signaling messages (see SignalingMessage model):
```json
{
  "type": "webrtc.signal",
  "data": {
    "type": "signaling-message-type",
    "data": {
      "offer": "...SDP string here...",
      "answer": "...SDP string here...",
      "candidate": {...ICE candidate data...}
    },
    "to": "optional-target-user-id-for-directed-messaging",
    "senderAccountId": "server-validated-user-guid",
    "senderInfo": {
      "id": "user-guid",
      "name": "username",
      "nick": "display nickname",
      "profile": {},
      "updatedAt": "2022-01-01T00:00:00Z"
    }
  }
}
```

For connection established:
```json
{
  "type": "webrtc",
  "data": {
    "userId": "user-guid",
    "roomId": "room-guid",
    "message": "Connected to call...",
    "timestamp": "2022-01-01T00:00:00Z",
    "participants": [...]
  }
}
```

#### 3.2 Incoming Messages

Implement a message handler to process signaling data with user identity:

```javascript
class SignalingClient {
    constructor(chatId, serverUrl = 'ws://localhost:5000', userId, userName) {
        this.chatId = chatId;
        this.ws = null;
        this.serverUrl = serverUrl;
        this.isConnected = false;
        this.userId = userId;          // Current user ID
        this.userName = userName;      // Current user name
        this.onMessageHandlers = [];
    }

    // ... WebSocket connection methods ...

    handleMessage(message) {
        try {
            // Parse WebSocketPacket
            const packet = JSON.parse(message);

            if (packet.type === 'signaling') {
                // Extract signaling message with server-validated sender info
                const signalingMessage = packet.data;
                const senderId = signalingMessage.SenderAccountId;
                const senderInfo = signalingMessage.SenderInfo;

                // Ignore messages from yourself (server broadcasts to all clients)
                if (senderId === this.userId) {
                    return;
                }

                // Use sender's nick or name for display
                const senderDisplay = senderInfo?.nick || senderInfo?.name || senderId;
                console.log(`Received ${signalingMessage.type} from ${senderDisplay} (${senderId})`);

                // Call handlers with signal type and data and sender info
                this.onMessageHandlers.forEach(handler => {
                    try {
                        handler(signalingMessage, senderId, senderInfo);
                    } catch (error) {
                        console.error('Error in message handler:', error);
                    }
                });
            } else if (packet.type === 'webrtc') {
                // Handle connection established or other server messages
                console.log('Received server message:', packet.data.message);
            } else {
                console.warn('Unknown packet type:', packet.type);
            }

        } catch (error) {
            console.error('Failed to parse WebSocketPacket:', message, error);
        }
    }

    // Register message handlers
    onMessage(handler) {
        this.onMessageHandlers.push(handler);
        return () => {
            // Return unsubscribe function
            const index = this.onMessageHandlers.indexOf(handler);
            if (index > -1) {
                this.onMessageHandlers.splice(index, 1);
            }
        };
    }

    sendMessage(messageData) {
        if (!this.isConnected || !this.ws || this.ws.readyState !== WebSocket.OPEN) {
            console.warn('Cannot send message: WebSocket not connected');
            return false;
        }

        try {
            // Server will automatically add sender info - just send the signaling data
            const messageStr = JSON.stringify(messageData);
            this.ws.send(messageStr);
            return true;
        } catch (error) {
            console.error('Failed to send message:', error);
            return false;
        }
    }
}
```

#### 3.3 User Identity Tracking

Track connected peers with full account information:

```javascript
class SignalingClient {
    constructor(chatId, serverUrl, userId, userName) {
        this.chatId = chatId;
        this.userId = userId;
        this.userName = userName;
        this.serverUrl = serverUrl;
        this.ws = null;
        this.isConnected = false;
        this.connectedPeers = new Map();  // userId -> senderInfo
        this.onPeerHandlers = [];
        this.onMessageHandlers = [];
    }

    handleMessage(message) {
        try {
            const packet = JSON.parse(message);

            if (packet.type === 'signaling') {
                const signalingMessage = packet.data;
                const senderId = signalingMessage.SenderAccountId;
                const senderInfo = signalingMessage.SenderInfo;

                // Track peer information with full account data
                if (!this.connectedPeers.has(senderId)) {
                    this.connectedPeers.set(senderId, senderInfo);
                    this.onPeerHandlers.forEach(handler => {
                        try {
                            handler(senderId, senderInfo, 'connected');
                        } catch (error) {
                            console.error('Error in peer handler:', error);
                        }
                    });
                    console.log(`New peer connected: ${senderInfo?.name || senderId} (${senderId})`);
                }

                // Ignore messages from yourself
                if (senderId === this.userId) {
                    return;
                }

                // Call handlers with signaling message and sender info
                this.onMessageHandlers.forEach(handler => {
                    try {
                        handler(signalingMessage, senderId, senderInfo);
                    } catch (error) {
                        console.error('Error in message handler:', error);
                    }
                });
            } else if (packet.type === 'webrtc') {
                // Handle connection established or other server messages
                console.log('Received server message:', packet.data.message);
            } else {
                console.warn('Unknown packet type:', packet.type);
            }

        } catch (error) {
            console.error('Failed to parse WebSocketPacket:', message, error);
        }
    }

    // Register peer connection/disconnection handlers
    onPeer(handler) {
        this.onPeerHandlers.push(handler);
        return () => {
            const index = this.onPeerHandlers.indexOf(handler);
            if (index > -1) {
                this.onPeerHandlers.splice(index, 1);
            }
        };
    }

    // Get list of connected peers with full account info
    getConnectedPeers() {
        return Array.from(this.connectedPeers.entries()).map(([userId, senderInfo]) => ({
            userId,
            userInfo: senderInfo
        }));
    }

    // Find user info by user ID
    getUserInfo(userId) {
        return this.connectedPeers.get(userId);
    }
}
```

### 4. WebRTC Integration

#### 4.1 Complete Implementation Example

```javascript
class WebRTCCPUB extends SignalingClient {
    constructor(chatId, serverUrl) {
        super(chatId, serverUrl);
        this.peerConnection = null;
        this.localStream = null;
        this.remoteStream = null;

        // Initialize WebRTCPeerConnection with configuration
        this.initPeerConnection();
    }

    initPeerConnection() {
        const configuration = {
            iceServers: [
                { urls: 'stun:stun.l.google.com:19302' },
                { urls: 'stun:stun1.l.google.com:19302' }
            ]
        };

        this.peerConnection = new RTCPeerConnection(configuration);

        // Handle ICE candidates
        this.peerConnection.onicecandidate = (event) => {
            if (event.candidate) {
                // Send ICE candidate via signaling server
                this.sendMessage({
                    type: 'ice-candidate',
                    candidate: event.candidate
                });
            }
        };

        // Handle remote stream
        this.peerConnection.ontrack = (event) => {
            this.remoteStream = event.streams[0];
            // Attach remote stream to video element
            if (this.onRemoteStream) {
                this.onRemoteStream(this.remoteStream);
            }
        };
    }

    // Register for signaling messages
    onMessage(signalingMessage, senderId, senderInfo) {
        super.onMessage(signalingMessage, senderId, senderInfo).then(() => {
            this.handleSignalingMessage(signalingMessage);
        });
    }

    handleSignalingMessage(signalingMessage) {
        switch (signalingMessage.type) {
            case 'offer':
                this.handleOffer(signalingMessage.data.offer);
                break;
            case 'answer':
                this.handleAnswer(signalingMessage.data.answer);
                break;
            case 'ice-candidate':
                this.handleIceCandidate(signalingMessage.data.candidate);
                break;
            default:
                console.warn('Unknown message type:', signalingMessage.type);
        }
    }

    async createOffer() {
        try {
            const offer = await this.peerConnection.createOffer();
            await this.peerConnection.setLocalDescription(offer);

            // Send offer via signaling server
            this.sendMessage({
                type: 'offer',
                offer: offer
            });

        } catch (error) {
            console.error('Error creating offer:', error);
        }
    }

    async handleOffer(offer) {
        try {
            await this.peerConnection.setRemoteDescription(new RTCSessionDescription(offer));
            const answer = await this.peerConnection.createAnswer();
            await this.peerConnection.setLocalDescription(answer);

            // Send answer via signaling server
            this.sendMessage({
                type: 'answer',
                answer: answer
            });

        } catch (error) {
            console.error('Error handling offer:', error);
        }
    }

    async handleAnswer(answer) {
        try {
            await this.peerConnection.setRemoteDescription(new RTCSessionDescription(answer));
        } catch (error) {
            console.error('Error handling answer:', error);
        }
    }

    async handleIceCandidate(candidate) {
        try {
            await this.peerConnection.addIceCandidate(new RTCIceCandidate(candidate));
        } catch (error) {
            console.error('Error handling ICE candidate:', error);
        }
    }

    // Get user media and add to peer connection
    async startLocalStream(constraints = { audio: true, video: true }) {
        try {
            this.localStream = await navigator.mediaDevices.getUserMedia(constraints);
            this.localStream.getTracks().forEach(track => {
                this.peerConnection.addTrack(track, this.localStream);
            });
            return this.localStream;
        } catch (error) {
            console.error('Error accessing media devices:', error);
            throw error;
        }
    }
}
```

### 5. Usage Flow

#### 5.1 Basic Usage Pattern

```javascript
// 1. Create signaling client
const signaling = new WebRTCCPUB(chatId, serverUrl);

// 2. Set up event handlers
signaling.onRemoteStream = (stream) => {
    // Attach remote stream to video element
    remoteVideoElement.srcObject = stream;
};

// 3. Connect to signaling server
await signaling.connect();

// 4. Get local media stream
await signaling.startLocalStream();

// 5. Create offer (for the caller)
await signaling.createOffer();

// The signaling server will automatically broadcast messages to other clients in the room
```

#### 5.2 Complete Call Flow Example

```javascript
async function initiateCall(chatId, serverUrl) {
    const caller = new WebRTCCPUB(chatId, serverUrl);

    // Connect to signaling server
    await caller.connect();

    // Get local stream
    const localStream = await caller.startLocalStream();
    localVideoElement.srcObject = localStream;

    // Create and send offer
    await caller.createOffer();

    // Wait for remote stream
    caller.onRemoteStream = (remoteStream) => {
        remoteVideoElement.srcObject = remoteStream;
        console.log('Call connected!');
    };
}

async function answerCall(chatId, serverUrl) {
    const answerer = new WebRTCCPUB(chatId, serverUrl);

    // Connect to signaling server
    await answerer.connect();

    // Get local stream
    const localStream = await answerer.startLocalStream();
    localVideoElement.srcObject = localStream;

    // WebRTC signaling is handled automatically by the message handlers
    answerer.onRemoteStream = (remoteStream) => {
        remoteVideoElement.srcObject = remoteStream;
        console.log('Call connected!');
    };
}
```

### 6. Error Handling

#### 6.1 Connection Errors

```javascript
// Handle connection errors
signaling.ws.addEventListener('error', (event) => {
    console.error('WebSocket connection error:', event);
    // Attempt reconnection or show error to user
});

// Handle server close
signaling.ws.addEventListener('close', (event) => {
    console.log('WebSocket closed:', event.code, event.reason);

    // Reconnect if clean closure
    if (event.wasClean) {
        // Re-establish connection if needed
    } else {
        // Report error
    }
});
```

#### 6.2 WebRTC Errors

```javascript
// Handle getUserMedia errors
try {
    const stream = await navigator.mediaDevices.getUserMedia(constraints);
} catch (error) {
    switch (error.name) {
        case 'NotAllowedError':
            console.error('User denied media access');
            break;
        case 'NotFoundError':
            console.error('No media devices found');
            break;
        default:
            console.error('Error accessing media:', error);
    }
}
```

### 7. Best Practices

#### 7.1 Connection Management
- **Reconnection Logic**: Implement exponential backoff for reconnection attempts
- **Connection Pooling**: Re-use connections when possible
- **Cleanup**: Always close connections and clean up resources

#### 7.2 Message Handling
- **Message Validation**: Validate incoming signaling messages
- **Error Resilience**: Gracefully handle malformed messages
- **Message Types**: Define clear message type conventions

#### 7.3 WebRTC Configuration
- **ICE Servers**: Configure multiple STUN/TURN servers for reliability
- **Codec Preferences**: Set preferred codecs for optimal performance
- **Bandwidth Management**: Implement appropriate bitrate controls

#### 7.4 Security Considerations
- **Input Validation**: Validate all signaling data
- **Rate Limiting**: Implement appropriate rate limiting for signaling messages
- **Authentication**: Ensure proper authentication before establishing connections

### 8. Room Isolation

The signaling server guarantees that:
- **Messages stay within rooms**: Clients only receive messages from other clients in the same chat room
- **Authentication per connection**: Each WebSocket connection is individually authenticated
- **Member validation**: Only active chat room members can connect and send messages

### 9. Troubleshooting

#### 9.1 Common Issues
- **Connection refused**: Check if JWT token is valid and user is room member
- **Messages not received**: Verify room membership and connection status
- **WebRTC failures**: Check ICE server configuration and network connectivity

#### 9.2 Debug Tips
- Enable console logging for signaling events
- Monitor WebSocket connection state
- Validate signaling message formats
- Check browser developer tools for network activity

## API Reference

### WebSocket Endpoint
- **URL Pattern**: `/api/chat/realtime/{chatId}`
- **Method**: `GET`
- **Authentication**: JWT (middleware-handled)
- **Protocol**: WebSocket (ws/wss)

### Response Codes
- **401**: Unauthorized - Invalid or missing JWT
- **403**: Forbidden - User not member of chat room
- **400**: Bad Request - Not a WebSocket request

### Message Format
- **Encoding**: UTF-8 text
- **Format**: WebSocketPacket JSON (server-enforced structure)
- **Broadcasting**: Automatic to all room members except sender with validated sender information

## Additional Resources

- [WebRTC API Documentation](https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API)
- [WebSocket API Documentation](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API)
- [WebRTC Signaling Fundamentals](https://webrtc.org/getting-started/signaling-channels)
