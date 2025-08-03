<template>
  <n-upload
    abstract
    with-credentials
    @remove="handleRemove"
    :create-thumbnail-url="createThumbnailUrl"
    :custom-request="customRequest"
    v-model:file-list="fileList"
  >
    <div class="flex flex-col gap-3">
      <pub-select v-model:value="publisher" />
      <n-input
        type="textarea"
        placeholder="What's happended?!"
        v-model:value="content"
        @keydown.meta.enter.exact="submit"
        @keydown.ctrl.enter.exact="submit"
      />
      <n-upload-file-list />
      <div class="flex justify-between">
        <div class="flex gap-2">
          <n-upload-trigger #="{ handleClick }" abstract>
            <n-button @click="handleClick">
              <n-icon><upload-round /></n-icon>
            </n-button>
          </n-upload-trigger>
        </div>
        <n-button type="primary" icon-placement="right" :loading="submitting" @click="submit">
          Post
          <template #icon>
            <n-icon><send-round /></n-icon>
          </template>
        </n-button>
      </div>
    </div>
  </n-upload>
</template>

<script setup lang="ts">
import {
  NInput,
  NButton,
  NIcon,
  NUpload,
  NUploadFileList,
  NUploadTrigger,
  useMessage,
  type UploadSettledFileInfo,
  type UploadCustomRequestOptions,
  create,
  type UploadFileInfo,
} from 'naive-ui'
import { SendRound, UploadRound } from '@vicons/material'
import { ref } from 'vue'
import * as tus from 'tus-js-client'

import PubSelect from './PubSelect.vue'

const emits = defineEmits(['posted'])

const publisher = ref<string | undefined>()
const content = ref('')

const fileList = ref<UploadFileInfo[]>([])

const submitting = ref(false)

async function submit() {
  submitting.value = true
  await fetch(`/api/posts?pub=${publisher.value}`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
    },
    body: JSON.stringify({
      content: content.value,
      attachments: fileList.value
        .filter((e) => e.url != null)
        .map((e) => e.url!.split('/').reverse()[0]),
    }),
  })

  submitting.value = false
  content.value = ''
  fileList.value = []
  emits('posted')
}

const messageDisplay = useMessage()

function customRequest({
  file,
  headers,
  withCredentials,
  onFinish,
  onError,
  onProgress,
}: UploadCustomRequestOptions) {
  const requestHeaders: Record<string, string> = {}
  const upload = new tus.Upload(file.file, {
    endpoint: '/cgi/drive/tus',
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
      file.url = `/cgi/drive/files/${jsonInfo.id}`
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

function handleRemove(data: { file: UploadFileInfo; fileList: UploadFileInfo[] }) {
  if (data.file.url == null) return true
  const messageReactive = messageDisplay.loading('Deleting files...', {
    duration: 0,
  })
  return new Promise((resolve) => {
    fetch(`/cgi/drive/files/${data.file.url!.split('/').reverse()[0]}`, { method: 'DELETE' })
      .then(() => {
        messageReactive.destroy()
        messageDisplay.success('File has been deleted')
        resolve(true)
      })
      .catch((err) => {
        messageReactive.destroy()
        messageDisplay.error('Unable to delete this file: ' + err)
        resolve(false)
      })
  })
}
</script>
