# Drive E2EE File Encryption

## Overview

Drive file encryption now uses E2EE key-based envelope encryption only.

- Password-based upload encryption is removed from request contract.
- Client provides a base64 E2EE key and optional envelope metadata.
- Drive stores encrypted blob and metadata, but does not decrypt content.

## Upload API Changes

`POST /api/files/upload/create` request fields:

```json
{
  "fileName": "example.bin",
  "fileSize": 1024,
  "contentType": "application/octet-stream",
  "hash": "sha256-or-other",
  "encryptKey": "BASE64_KEY",
  "encryptionScheme": "pass.e2ee.file.raw-key.v1",
  "encryptionHeader": "BASE64_HEADER",
  "encryptionSignature": "BASE64_SIGNATURE",
  "encryptionEpoch": 1
}
```

Notes:
- `encryptKey` must be valid base64 if provided.
- If `encryptKey` is omitted, upload remains plaintext (existing behavior).
- Pool policy `allowEncryption=false` still blocks encrypted upload.

## Envelope Format

Encrypted file output format:

1. Magic: `DYE2EE1\0`
2. Version byte
3. Salt length byte (currently `0` for raw-key mode)
4. Salt bytes (optional)
5. AES-GCM nonce (12 bytes)
6. AES-GCM tag (16 bytes)
7. JSON header length (`int32`)
8. JSON header bytes
9. Ciphertext bytes

Header JSON includes:
- `encryptionScheme`
- `encryptionEpoch`
- `encryptionHeader` (base64, optional)
- `encryptionSignature` (base64, optional)
- `kdf` (`none` for raw-key mode)

## Stored Metadata

Drive writes E2EE metadata into `SnFileObject.Meta["e2ee"]`:
- `scheme`
- `epoch`
- `header` (base64 or null)
- `signature` (base64 or null)

## Compatibility

- Newly encrypted files use E2EE envelope format only.
- Legacy password-based decrypt flow is removed from `FileEncryptor`.
