<template>
  <section class="min-h-full relative flex items-center justify-center">
    <n-spin v-if="!fileInfo && !error" />
    <n-result status="404" title="No file was found" :description="error" v-else-if="error" />
    <n-card class="max-w-4xl my-4 mx-8" v-else>
      <n-grid cols="1 m:2" x-gap="16" y-gap="16" responsive="screen">
        <n-gi>
          <div v-if="fileInfo.is_encrypted">
            <n-alert type="info" size="small" title="Encrypted file">
              The file has been encrypted. Preview not available. Please enter the password to
              download it.
            </n-alert>
          </div>
          <div v-else>
            <n-image v-if="fileType === 'image'" :src="fileSource" class="w-full" />
            <video v-else-if="fileType === 'video'" :src="fileSource" controls class="w-full" />
            <audio v-else-if="fileType === 'audio'" :src="fileSource" controls class="w-full" />
            <n-result
              status="418"
              title="Preview Unavailable"
              description="How can you preview this file?"
              size="small"
              class="py-6"
              v-else
            />
          </div>
        </n-gi>

        <n-gi>
          <div class="mb-3">
            <n-card title="File Infomation" size="small">
              <div class="flex gap-2">
                <span class="flex-grow-1 flex items-center gap-2">
                  <n-icon>
                    <info-round />
                  </n-icon>
                  File Type
                </span>
                <span>{{ fileInfo.mime_type }} ({{ fileType }})</span>
              </div>
              <div class="flex gap-2">
                <span class="flex-grow-1 flex items-center gap-2">
                  <n-icon>
                    <data-usage-round />
                  </n-icon>
                  File Size
                </span>
                <span>{{ formatBytes(fileInfo.size) }}</span>
              </div>
              <div class="flex gap-2">
                <span class="flex-grow-1 flex items-center gap-2">
                  <n-icon>
                    <file-upload-outlined />
                  </n-icon>
                  Uploaded At
                </span>
                <span>{{ new Date(fileInfo.created_at).toLocaleString() }}</span>
              </div>
              <div class="flex gap-2">
                <span class="flex-grow-1 flex items-center gap-2">
                  <n-icon>
                    <details-round />
                  </n-icon>
                  Techical Info
                </span>
                <n-button text size="small" @click="showTechDetails = !showTechDetails">
                  {{ showTechDetails ? 'Hide' : 'Show' }}
                </n-button>
              </div>

              <n-collapse-transition :show="showTechDetails">
                <div v-if="showTechDetails" class="mt-2 flex flex-col gap-1">
                  <p class="text-xs opacity-75">#{{ fileInfo.id }}</p>

                  <n-card size="small" content-style="padding: 0" embedded>
                    <div class="overflow-x-auto px-4 py-2">
                      <n-code
                        :code="JSON.stringify(fileInfo.file_meta, null, 4)"
                        language="json"
                        :hljs="hljs"
                      />
                    </div>
                  </n-card>
                </div>
              </n-collapse-transition>
            </n-card>
          </div>

          <div class="flex flex-col gap-3">
            <n-input
              v-if="fileInfo.is_encrypted"
              placeholder="Password"
              v-model:value="filePass"
              type="password"
            />
            <div class="flex gap-2">
              <n-button class="flex-grow-1" @click="downloadFile">Download</n-button>
              <n-popover placement="bottom" trigger="hover">
                <template #trigger>
                  <n-button>
                    <n-icon>
                      <qr-code-round />
                    </n-icon>
                  </n-button>
                </template>
                <n-qr-code
                  type="svg"
                  :value="currentUrl"
                  :size="160"
                  icon-src="/favicon.png"
                  error-correction-level="H"
                />
              </n-popover>
            </div>
          </div>
          <n-collapse-transition :show="!!progress">
            <n-progress
              :processing="!!progress && progress < 100"
              :percentage="progress"
              indicator-placement="inside"
              class="mt-4"
            />
          </n-collapse-transition>
        </n-gi>
      </n-grid>
    </n-card>
  </section>
</template>

<script setup lang="ts">
import {
  NCard,
  NInput,
  NButton,
  NProgress,
  NResult,
  NSpin,
  NImage,
  NAlert,
  NIcon,
  NCollapseTransition,
  NCode,
  NGrid,
  NGi,
  NPopover,
  NQrCode,
  useMessage,
} from 'naive-ui'
import {
  DataUsageRound,
  InfoRound,
  DetailsRound,
  FileUploadOutlined,
  QrCodeRound,
} from '@vicons/material'
import { useRoute } from 'vue-router'
import { computed, onMounted, ref } from 'vue'

import { downloadAndDecryptFile } from './secure'
import { formatBytes } from './format'

import hljs from 'highlight.js/lib/core'
import json from 'highlight.js/lib/languages/json'

hljs.registerLanguage('json', json)

const route = useRoute()

const error = ref<string | null>(null)

const filePass = ref<string>('')
const fileId = route.params.fileId
const passcode = route.query.passcode as string | undefined

const progress = ref<number | undefined>(0)

const showTechDetails = ref<boolean>(false)

const messageDisplay = useMessage()

const currentUrl = window.location.href

const fileInfo = ref<any>(null)
async function fetchFileInfo() {
  try {
    let url = '/api/files/' + fileId + '/info'
    if (passcode) {
      url += `?passcode=${passcode}`
    }
    const resp = await fetch(url)
    if (!resp.ok) {
      throw new Error('Failed to fetch file info: ' + resp.statusText)
    }
    fileInfo.value = await resp.json()
  } catch (err) {
    error.value = (err as Error).message
  }
}
onMounted(() => fetchFileInfo())

const fileType = computed(() => {
  if (!fileInfo.value) return 'unknown'
  return fileInfo.value.mime_type?.split('/')[0] || 'unknown'
})
const fileSource = computed(() => {
  let url = `/api/files/${fileId}`
  if (passcode) {
    url += `?passcode=${passcode}`
  }
  return url
})

async function downloadFile() {
  if (fileInfo.value.is_encrypted && !filePass.value) {
    messageDisplay.error('Please enter the password to download the file.')
    return
  }
  if (fileInfo.value.is_encrypted) {
    downloadAndDecryptFile(fileSource.value, filePass.value, fileInfo.value.name, (p: number) => {
      progress.value = p * 100
    }).catch((err) => {
      messageDisplay.error('Download failed: ' + err.message, { closable: true, duration: 10000 })
      progress.value = undefined
    })
  } else {
    const res = await fetch(fileSource.value)
    if (!res.ok) {
      throw new Error(`Failed to download ${fileInfo.value.name}: ${res.statusText}`)
    }

    const contentLength = res.headers.get('content-length')
    if (!contentLength) {
      throw new Error('Content-Length response header is missing.')
    }

    const total = parseInt(contentLength, 10)
    const reader = res.body!.getReader()
    const chunks: Uint8Array[] = []
    let received = 0

    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      if (value) {
        chunks.push(value)
        received += value.length
        progress.value = (received / total) * 100
      }
    }

    const blob = new Blob(chunks)
    const blobUrl = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = blobUrl
    a.download = fileInfo.value.name || 'download'
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.URL.revokeObjectURL(blobUrl)
  }
}
</script>
