<template>
  <div>
    <n-collapse-transition :show="showRecycleHint">
      <n-alert size="small" type="warning" title="Recycle Enabled" class="mb-3">
        You're uploading to a pool which enabled recycle. If the file you uploaded didn't referenced
        from the Solar Network. It will be marked and will be deleted some while later.
      </n-alert>
    </n-collapse-transition>

    <n-collapse-transition :show="modeAdvanced">
      <n-card title="Advance Options" size="small" class="mb-3">
        <div class="flex flex-col gap-3">
          <div>
            <p class="pl-1 mb-0.5">File Password</p>
            <n-input
              v-model:value="filePass"
              :disabled="!currentFilePool?.allow_encryption"
              placeholder="Enter password to protect the file"
              show-password-toggle
              size="large"
              type="password"
              class="mb-2"
            />
            <p class="pl-1 text-xs opacity-75 mt-[-4px]">
              Only available for Stellar Program and certian file pool.
            </p>
          </div>
          <div>
            <p class="pl-1 mb-0.5">File Expiration Date</p>
            <n-date-picker
              v-model:value="fileExpire"
              type="datetime"
              clearable
              :is-date-disabled="disablePreviousDate"
            />
          </div>
          <div
            v-if="currentFilePool?.policy_config?.enable_fast_upload || route.query.pool"
            class="flex items-center gap-2"
          >
            <p class="pl-1 mb-0.5">Fast Upload</p>
            <n-switch v-model:value="fastUpload" />
          </div>
        </div>
      </n-card>
    </n-collapse-transition>

    <n-upload
      multiple
      directory-dnd
      with-credentials
      show-preview-button
      list-type="image"
      show-download-button
      :custom-request="customRequest"
      :custom-download="customDownload"
      :create-thumbnail-url="createThumbnailUrl"
      @preview="customPreview"
    >
      <n-upload-dragger>
        <div style="margin-bottom: 12px">
          <n-icon size="48" :depth="3">
            <cloud-upload-round />
          </n-icon>
        </div>
        <n-text style="font-size: 16px"> Click or drag a file to this area to upload </n-text>
        <n-p depth="3" style="margin: 8px 0 0 0">
          Strictly prohibit from uploading sensitive information. For example, your bank card PIN or
          your credit card expiry date.
        </n-p>
      </n-upload-dragger>
    </n-upload>
  </div>
</template>

<script setup lang="ts">
import {
  NUpload,
  NUploadDragger,
  NIcon,
  NText,
  NP,
  NInput,
  NCollapseTransition,
  NDatePicker,
  NAlert,
  NCard,
  NSwitch,
  type UploadCustomRequestOptions,
  type UploadSettledFileInfo,
  type UploadFileInfo,
  useMessage,
} from 'naive-ui'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { CloudUploadRound } from '@vicons/material'
import type { SnFilePool } from '@/types/pool'

import * as tus from 'tus-js-client'

const props = defineProps<{
  filePool: string | null
  modeAdvanced: boolean
  pools: SnFilePool[]
  bundleId?: string
}>()

const route = useRoute()

const filePass = ref<string>('')
const fileExpire = ref<number | null>(null)
const fastUpload = ref<boolean>(false)

const effectiveFilePool = computed(() => (route.query.pool as string) || props.filePool)

const currentFilePool = computed(() => {
  if (!effectiveFilePool.value) return null
  return props.pools?.find((pool) => pool.id === effectiveFilePool.value) ?? null
})
const showRecycleHint = computed(() => {
  if (!effectiveFilePool.value) return true
  return currentFilePool.value?.policy_config?.enable_recycle || false
})

const messageDisplay = useMessage()

async function customRequest({
  file,
  headers,
  withCredentials,
  onFinish,
  onError,
  onProgress,
}: UploadCustomRequestOptions) {
  if (fastUpload.value) {
    const hash = await crypto.subtle.digest('SHA-256', await file.file!.arrayBuffer())
    const hashString = Array.from(new Uint8Array(hash))
      .map((b) => b.toString(16).padStart(2, '0'))
      .join('')

    const resp = await fetch('/api/files/fast', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: file.name,
        size: file.file?.size,
        hash: hashString,
        mime_type: file.file?.type,
        pool_id: effectiveFilePool.value,
      }),
    })

    if (!resp.ok) {
      messageDisplay.error(`Failed to get presigned URL: ${await resp.text()}`)
      onError()
      return
    }

    const respData = await resp.json()
    const url = respData.fast_upload_link

    try {
      const xhr = new XMLHttpRequest()
      xhr.open('PUT', url, true)
      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable) {
          onProgress({ percent: (event.loaded / event.total) * 100 })
        }
      }
      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          onFinish()
        } else {
          messageDisplay.error(`Upload failed: ${xhr.responseText}`)
          onError()
        }
      }
      xhr.onerror = () => {
        messageDisplay.error('Upload failed due to a network error.')
        onError()
      }
      xhr.send(file.file)
    } catch (e) {
      console.error(e)
      messageDisplay.error(`Upload failed: ${e}`)
      onError()
    }
    return
  }

  const requestHeaders: Record<string, string> = {}
  if (effectiveFilePool.value) requestHeaders['X-FilePool'] = effectiveFilePool.value
  if (filePass.value) requestHeaders['X-FilePass'] = filePass.value
  if (fileExpire.value) requestHeaders['X-FileExpire'] = fileExpire.value.toString()
  if (props.bundleId) requestHeaders['X-FileBundle'] = props.bundleId
  const upload = new tus.Upload(file.file as any, {
    endpoint: '/api/tus',
    retryDelays: [0, 3000, 5000, 10000, 20000],
    removeFingerprintOnSuccess: false,
    uploadDataDuringCreation: false,
    metadata: {
      filename: file.name,
      'content-type': file.type ?? 'application/octet-stream',
    },
    headers: {
      'X-DirectUpload': 'true',
      ...requestHeaders,
      ...headers,
    },
    onShouldRetry: () => false,
    onError: function (error) {
      if (error instanceof tus.DetailedError) {
        const failedBody = error.originalResponse?.getBody()
        if (failedBody != null)
          messageDisplay.error(`Upload failed: ${failedBody}`, {
            duration: 10000,
            closable: true,
          })
      }
      console.error('[DRIVE] Upload failed:', error)
      onError()
    },
    onProgress: function (bytesUploaded, bytesTotal) {
      onProgress({ percent: (bytesUploaded / bytesTotal) * 100 })
    },
    onSuccess: function (payload) {
      const rawInfo = payload.lastResponse.getHeader('x-fileinfo')
      const jsonInfo = JSON.parse(rawInfo as string)
      console.log('[DRIVE] Upload successful: ', jsonInfo)
      file.url = `/api/files/${jsonInfo.id}`
      file.type = jsonInfo.mime_type
      onFinish()
    },
    onBeforeRequest: function (req) {
      const xhr = req.getUnderlyingObject()
      xhr.withCredentials = withCredentials
    },
  })
  upload.findPreviousUploads().then(function (previousUploads) {
    if (previousUploads.length) {
      upload.resumeFromPreviousUpload(previousUploads[0])
    }
    upload.start()
  })
}

function createThumbnailUrl(
  _file: File | null,
  fileInfo: UploadSettledFileInfo,
): string | undefined {
  if (!fileInfo) return undefined
  return fileInfo.url ?? undefined
}

function customDownload(file: UploadFileInfo) {
  const { url } = file
  if (!url) return
  window.open(url.replace('/api', ''), '_blank')
}

function customPreview(file: UploadFileInfo, detail: { event: MouseEvent }) {
  detail.event.preventDefault()
  const { url } = file
  if (!url) return
  window.open(url.replace('/api', ''), '_blank')
}

function disablePreviousDate(ts: number) {
  return ts <= Date.now()
}
</script>
