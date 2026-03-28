# Drive E2EE Client Migration Guide

## Who should read this

Any Drive client (web/mobile/desktop) that uploads or downloads E2EE files.

## Breaking changes

1. Do not send `encryptKey` in `POST /api/files/upload/create`.
2. Do not send `encryptionEpoch` for file upload metadata.
3. For E2EE upload metadata, send:
   - `encryptionScheme` (for example `file.aesgcm.v1`)
   - `encryptionHeader` (base64, required when scheme is present)
   - `encryptionSignature` (base64, optional)
4. If `encryptionScheme` is present but `encryptionHeader` is missing, server rejects request.
5. `encryptKey` is now rejected with `400`.

## Required client behavior

1. Encrypt file locally before upload.
2. Keep file key only in secure client storage.
3. Put wrapped key material into `encryptionHeader` (base64).
4. Upload ciphertext bytes via existing chunk upload flow.
5. Use MLS chat channel (`chat.mls.v2`) for sharing file keys with other users/devices.

## Upload request example

```json
{
  "fileName": "secret.pdf",
  "fileSize": 123456,
  "contentType": "application/octet-stream",
  "hash": "sha256:ciphertext_hash",
  "encryptionScheme": "file.aesgcm.v1",
  "encryptionHeader": "BASE64_WRAPPED_KEY_OR_HEADER",
  "encryptionSignature": "BASE64_OPTIONAL_SIGNATURE"
}
```

## Read metadata endpoint

Use this endpoint before decrypt if needed:

- `GET /api/files/{id}/e2ee`

Response:

```json
{
  "scheme": "file.aesgcm.v1",
  "header": "base64-or-null",
  "signature": "base64-or-null"
}
```

## Client crypto requirements

1. File key must be cryptographically random 32 bytes.
2. Never reuse nonce for the same key.
3. If using signature, prefer `Ed25519(header_bytes || file_hash)`.
4. Treat server metadata as untrusted input; verify/decrypt client-side.

## Rollout checklist

1. Remove `encryptKey` writes from all clients.
2. Remove `encryptionEpoch` writes from all clients.
3. Add request validation on client side for required `encryptionHeader` with scheme.
4. Add decrypt flow that can consume `/api/files/{id}/e2ee`.
5. Add telemetry:
   - upload `400` validation errors for e2ee payload
   - decrypt failures (`missing_header`, `invalid_header`, `invalid_ciphertext`)

## Compatibility note

Old files remain readable if client still has the correct key material. Existing server data is not auto-re-encrypted.
