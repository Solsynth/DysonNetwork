<template>
  <section class="h-full relative flex items-center justify-center">
    <n-card class="max-w-lg" title="About" v-if="!userStore.user">
      <p>Welcome to the <b>Solar Drive</b></p>
      <p>We help you upload, collect, and share files with ease in mind.</p>
      <p>To continue, login first.</p>

      <p class="mt-4 opacity-75 text-xs">
        <span v-if="version == null">Loading...</span>
        <span v-else>
          v{{ version.version }} @
          {{ version.commit.substring(0, 6) }}
          {{ version.updatedAt }}
        </span>
      </p>
    </n-card>
    <n-card class="max-w-2xl" title="Upload to Solar Network" v-else>
      <template #header-extra>
        <div class="flex gap-2 items-center">
          <p>Advance Mode</p>
          <n-switch v-model:value="modeAdvanced" size="small" />
        </div>
      </template>

      <div class="mb-3" v-if="modeAdvanced">
        <n-input
          v-model:value="filePass"
          placeholder="Enter password to protect the file"
          clearable
          size="large"
          type="password"
          class="mb-2"
        />
      </div>

      <n-upload
        multiple
        directory-dnd
        with-credentials
        show-preview-button
        list-type="image"
        :custom-request="customRequest"
        :create-thumbnail-url="createThumbnailUrl"
      >
        <n-upload-dragger>
          <div style="margin-bottom: 12px">
            <n-icon size="48" :depth="3">
              <upload-outlined />
            </n-icon>
          </div>
          <n-text style="font-size: 16px"> Click or drag a file to this area to upload </n-text>
          <n-p depth="3" style="margin: 8px 0 0 0">
            Strictly prohibit from uploading sensitive information. For example, your bank card PIN
            or your credit card expiry date.
          </n-p>
        </n-upload-dragger>
      </n-upload>

      <p class="mt-4 opacity-75 text-xs">
        <span v-if="version == null">Loading...</span>
        <span v-else>
          v{{ version.version }} @
          {{ version.commit.substring(0, 6) }}
          {{ version.updatedAt }}
        </span>
      </p>
    </n-card>
  </section>
</template>

<script setup lang="ts">
import {
  NCard,
  NUpload,
  NUploadDragger,
  NIcon,
  NText,
  NP,
  NInput,
  NSwitch,
  type UploadCustomRequestOptions,
  type UploadSettledFileInfo,
} from 'naive-ui'
import { onMounted, ref } from 'vue'
import { UploadOutlined } from '@vicons/material'
import { useUserStore } from '@/stores/user'

import * as tus from 'tus-js-client'

const userStore = useUserStore()

const version = ref<any>(null)

async function fetchVersion() {
  const resp = await fetch('/api/version')
  version.value = await resp.json()
}

onMounted(() => fetchVersion())

const modeAdvanced = ref(false)

const filePass = ref<string>('')

function customRequest({
  file,
  data,
  headers,
  withCredentials,
  action,
  onFinish,
  onError,
  onProgress,
}: UploadCustomRequestOptions) {
  const upload = new tus.Upload(file.file, {
    endpoint: '/api/tus',
    retryDelays: [0, 3000, 5000, 10000, 20000],
    metadata: {
      filename: file.name,
      filetype: file.type ?? 'application/octet-stream',
    },
    headers: {
      'X-FilePass': filePass.value,
      ...headers,
    },
    onError: function (error) {
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

function createThumbnailUrl(_file: File | null, fileInfo: UploadSettledFileInfo): string | undefined {
  if (!fileInfo) return undefined
  return fileInfo.url ?? undefined
}
</script>
