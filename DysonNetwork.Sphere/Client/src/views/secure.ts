export async function downloadAndDecryptFile(
  url: string,
  password: string,
  fileName: string,
  onProgress?: (progress: number) => void,
): Promise<void> {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`Failed to fetch: ${response.status}`)

  const contentLength = +(response.headers.get('Content-Length') || 0)
  const reader = response.body!.getReader()
  const chunks: Uint8Array[] = []
  let received = 0

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    if (value) {
      chunks.push(value)
      received += value.length
      if (contentLength && onProgress) {
        onProgress(received / contentLength)
      }
    }
  }

  const fullBuffer = new Uint8Array(received)
  let offset = 0
  for (const chunk of chunks) {
    fullBuffer.set(chunk, offset)
    offset += chunk.length
  }

  const decryptedBytes = await decryptFile(fullBuffer, password)

  // Create a blob and trigger a download
  const blob = new Blob([decryptedBytes])
  const downloadUrl = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = downloadUrl
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(downloadUrl)
}

export async function decryptFile(fileBuffer: Uint8Array, password: string): Promise<Uint8Array> {
  const salt = fileBuffer.slice(0, 16)
  const nonce = fileBuffer.slice(16, 28)
  const tag = fileBuffer.slice(28, 44)
  const ciphertext = fileBuffer.slice(44)

  const enc = new TextEncoder()
  const keyMaterial = await crypto.subtle.importKey(
    'raw',
    enc.encode(password),
    { name: 'PBKDF2' },
    false,
    ['deriveKey'],
  )
  const key = await crypto.subtle.deriveKey(
    { name: 'PBKDF2', salt, iterations: 100000, hash: 'SHA-256' },
    keyMaterial,
    { name: 'AES-GCM', length: 256 },
    false,
    ['decrypt'],
  )

  const fullCiphertext = new Uint8Array(ciphertext.length + tag.length)
  fullCiphertext.set(ciphertext)
  fullCiphertext.set(tag, ciphertext.length)

  let decrypted: ArrayBuffer
  try {
    decrypted = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: nonce, tagLength: 128 },
      key,
      fullCiphertext,
    )
  } catch {
    throw new Error('Incorrect password or corrupted file.')
  }

  const magic = new TextEncoder().encode('DYSON1')
  const decryptedBytes = new Uint8Array(decrypted)
  for (let i = 0; i < magic.length; i++) {
    if (decryptedBytes[i] !== magic[i]) {
      throw new Error('Incorrect password or corrupted file.')
    }
  }

  return decryptedBytes.slice(magic.length)
}
