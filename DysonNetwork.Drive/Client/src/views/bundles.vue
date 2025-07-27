<template>
  <section class="min-h-full relative flex items-center justify-center">
    <n-spin v-if="!bundleInfo && !error" />
    <n-result
      status="404"
      title="No bundle was found"
      :description="error"
      v-else-if="error === '404'"
    />

    <n-card class="max-w-md my-4 mx-8" v-else-if="error === '403'">
      <n-result
        status="403"
        title="Access Denied"
        description="This bundle is protected by a passcode"
        class="mt-5 mb-2"
      >
        <template #footer>
          <n-alert v-if="passcodeError" type="error" class="mb-3">
            {{ passcodeError }}
          </n-alert>
          <n-input
            v-model:value="passcode"
            type="password"
            show-password-on="mousedown"
            placeholder="Passcode"
            @keyup.enter="fetchBundleInfo"
            class="mb-3"
          />
          <n-button type="primary" block @click="fetchBundleInfo">Access Bundle</n-button>
        </template>
      </n-result>
    </n-card>

    <n-card class="max-w-4xl my-4 mx-8" v-else>
      <n-grid cols="1 m:2" x-gap="16" y-gap="16" responsive="screen">
        <n-gi>
          <n-card title="Content" size="small">
            <n-list
              size="small"
              v-if="bundleInfo.files && bundleInfo.files.length > 0"
              style="padding: 0"
            >
              <n-list-item v-for="file in bundleInfo.files" :key="file.id">
                <n-thing :title="file.name" :description="formatBytes(file.size)">
                  <template #header-extra>
                    <n-button text type="primary" @click="goToFileDetails(file.id)">View</n-button>
                  </template>
                </n-thing>
              </n-list-item>
            </n-list>
            <n-empty v-else description="No files in this bundle" />
            <template #footer>
              <n-collapse-transition :show="!!downloadProgress">
                <n-progress
                  type="line"
                  :percentage="downloadProgress"
                  indicator-placement="inside"
                  :status="downloadStatus"
                  processing
                  class="mb-4"
                />
              </n-collapse-transition>
              <n-button
                type="primary"
                block
                :disabled="!bundleInfo.files || bundleInfo.files.length === 0 || downloading"
                @click="downloadAllFiles"
              >
                Download All
              </n-button>
            </template>
          </n-card>
        </n-gi>

        <n-gi>
          <n-card size="small">
            <h3 class="text-lg">{{ bundleInfo.name }}</h3>
            <p class="mb-3" v-if="bundleInfo.description">{{ bundleInfo.description }}</p>
            <div class="flex gap-2">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <calendar-today-round />
                </n-icon>
                Expires At
              </span>
              <span>{{
                bundleInfo.expiredAt ? new Date(bundleInfo.expiredAt).toLocaleString() : 'Never'
              }}</span>
            </div>
            <div class="flex gap-2">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <lock-round />
                </n-icon>
                Passcode Protected
              </span>
              <span>{{ bundleInfo.passcode ? 'Yes' : 'No' }}</span>
            </div>
          </n-card>
          <n-input
            v-model:value="filePass"
            type="password"
            size="large"
            placeholder="File password file decrypt"
            class="mt-3"
          />
        </n-gi>
      </n-grid>
    </n-card>
  </section>
</template>

<script setup lang="ts">
import {
  NCard,
  NResult,
  NSpin,
  NIcon,
  NGrid,
  NGi,
  NList,
  NListItem,
  NThing,
  NButton,
  NEmpty,
  NInput,
  NAlert,
  NProgress,
  NCollapseTransition,
  useMessage,
} from 'naive-ui'
import { CalendarTodayRound, LockRound } from '@vicons/material'
import { useRoute, useRouter } from 'vue-router'
import { onMounted, ref, watch } from 'vue'

import { formatBytes } from './format' // Assuming format.ts is in the same directory
import { downloadAndDecryptFile } from './secure'

const route = useRoute()
const router = useRouter()

const error = ref<string | null>(null)
const bundleId = route.params.bundleId
const passcode = ref<string>('')
const passcodeError = ref<string | null>(null)

const filePass = ref<string>('')

const downloading = ref(false)
const downloadProgress = ref<number | undefined>()
const downloadStatus = ref<'success' | 'error' | 'info'>('info')

watch(
  route,
  (value) => {
    if (value.query.passcode) passcode.value = value.query.passcode.toString()
  },
  { immediate: true, deep: true },
)

const bundleInfo = ref<any>(null)
async function fetchBundleInfo() {
  try {
    let url = '/api/bundles/' + bundleId
    if (passcode.value) {
      url += `?passcode=${passcode.value}`
    }
    const resp = await fetch(url)
    if (resp.status === 403) {
      error.value = '403'
      bundleInfo.value = null
      if (passcode.value) {
        passcodeError.value = 'Incorrect passcode.'
      }
      return
    }
    if (!resp.ok) {
      throw new Error('Failed to fetch bundle info: ' + resp.statusText)
    }
    bundleInfo.value = await resp.json()
    error.value = null
    passcodeError.value = null
  } catch (err) {
    error.value = (err as Error).message
  }
}
onMounted(() => fetchBundleInfo())

function goToFileDetails(fileId: string) {
  router.push({ path: `/files/${fileId}`, query: { passcode: passcode.value } })
}

const messageDisplay = useMessage()

async function downloadAllFiles() {
  if (!bundleInfo.value || !bundleInfo.value.files || bundleInfo.value.files.length === 0) {
    return
  }

  downloading.value = true
  downloadProgress.value = 0
  downloadStatus.value = 'info'

  const totalFiles = bundleInfo.value.files.length
  let completedDownloads = 0

  for (const file of bundleInfo.value.files) {
    let url = `/api/files/${file.id}`
    if (passcode.value) {
      url += `?passcode=${passcode.value}`
    }

    if (file.is_encrypted) {
      downloadAndDecryptFile(file, filePass.value, file.name, () => {})
        .catch((err) => {
          messageDisplay.error('Download failed: ' + err.message, {
            closable: true,
            duration: 10000,
          })
        })
        .finally(() => {
          completedDownloads++
          downloadProgress.value = (completedDownloads / totalFiles) * 100
        })
    } else {
      try {
        const res = await fetch(url)
        if (!res.ok) {
          throw new Error(`Failed to download ${file.name}: ${res.statusText}`)
        }
        const blob = await res.blob()
        const blobUrl = window.URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = blobUrl
        a.download = file.name || 'download' // fallback name
        document.body.appendChild(a)
        a.click()
        a.remove()
        window.URL.revokeObjectURL(blobUrl)

        if (completedDownloads === totalFiles) {
          downloadStatus.value = 'success'
        }
      } catch (err) {
        messageDisplay.error(`Download failed for ${file.name}: ${err}`)
        downloadStatus.value = 'error'
      } finally {
        completedDownloads++
        downloadProgress.value = (completedDownloads / totalFiles) * 100
      }
    }
  }
}
</script>
