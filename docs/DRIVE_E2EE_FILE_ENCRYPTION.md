# Drive E2EE File Encryption

## Overview

Drive now treats file encryption as client-side only.

- Password-based upload encryption is removed from request contract.
- Raw upload key (`encryptKey`) is removed from effective API behavior and rejected if sent.
- Client encrypts file locally and uploads ciphertext bytes.
- Drive stores ciphertext and opaque E2EE metadata, but never receives file keys.
- Scheme naming now follows `<usecase>.<method>.<version>` (for example `file.aesgcm.v1`).

## Algorithm

- Recommended client file payload encryption: `AES-256-GCM` with random 12-byte nonce.
- Recommended key generation: crypto-random 32-byte key (`crypto.getRandomValues` or equivalent).
- Key distribution: wrapped key material should be carried in `encryptionHeader` (base64), not via Drive key fields.
- For shared files, key distribution should use MLS chat control messages (`chat.mls.v1`) rather than Drive upload APIs.
- Recommended signature semantics: `encryptionSignature = Ed25519(header_bytes || file_hash)` (client-verifiable).

## Upload API Changes

`POST /api/files/upload/create` request fields:

```json
{
  "fileName": "example.bin",
  "fileSize": 1024,
  "contentType": "application/octet-stream",
  "hash": "sha256-or-other",
  "encryptionScheme": "file.aesgcm.v1",
  "encryptionHeader": "BASE64_HEADER",
  "encryptionSignature": "BASE64_SIGNATURE"
}
```

Notes:
- `encryptKey` is rejected (`400`) if provided.
- If no encryption metadata is provided, upload is treated as plaintext.
- Pool policy `allowEncryption=false` still blocks encrypted upload.
- `encryptionScheme` is required when `encryptionHeader` is present.
- `encryptionHeader` and `encryptionSignature` must be valid base64 if present.

## Envelope Format

Drive does not generate encryption envelopes during upload. It accepts client-produced ciphertext.

Reference envelope format for client/tooling (`FileEncryptor`) is:

1. Magic: `DYE2EE1\0`
2. Version byte
3. Salt length byte
4. Salt bytes (HKDF salt)
5. AES-GCM nonce (12 bytes)
6. JSON header length (`int32`)
7. JSON header bytes (AAD)
8. Ciphertext bytes
9. AES-GCM tag (16 bytes)

Header JSON includes:
- `encryptionScheme`
- `encryptionHeader` (base64, optional)
- `encryptionSignature` (base64, optional)
- `kdf` (`hkdf-sha256`)

Security note:
- Header bytes are authenticated as GCM AAD, so header tampering invalidates tag verification.

## Stored Metadata

Drive writes E2EE metadata into `SnFileObject.Meta["e2ee"]`:
- `scheme`
- `header` (base64 or null)
- `signature` (base64 or null)

## E2EE Metadata Endpoint

- `GET /api/files/{id}/e2ee`
- Returns E2EE metadata for authorized readers only:
  - `scheme`
  - `header`
  - `signature`
- No key material is returned.

## Compatibility

- Legacy password-based flows are removed.
- Legacy uploads that previously used server-side raw-key encryption are still stored as ciphertext objects; they are not re-encrypted automatically.
- Migration recommendation: client-side re-encrypt legacy files and upload new objects with modern metadata.

## Threat Model

Protected:

- file body confidentiality when client encrypts before upload (server stores ciphertext bytes)
- envelope/header integrity for client/tooling that uses AAD-authenticated envelope format

Not protected:

- server-visible metadata (filename, size, mime type, hash, e2ee scheme/header/signature, timing)
- access pattern leakage (who accesses which file and when)

Attack surface / assumptions:

- nonce reuse is a client encryption bug and can break confidentiality
- compromised client devices can leak local file keys before revoke/rotation workflows complete
- legacy files are not auto-migrated; operator/client migration is required for uniform guarantees
