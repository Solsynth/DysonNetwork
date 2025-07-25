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

      <div class="mb-3">
        <file-pool-select v-model="filePool" @update:pool="currentFilePool = $event" />
      </div>

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
  NSelect,
  NTag,
  NCollapseTransition,
  NDatePicker,
  type UploadCustomRequestOptions,
  type UploadSettledFileInfo,
  type SelectOption,
  type SelectRenderTag,
  type UploadFileInfo,
  useMessage,
  NDivider,
  NTooltip,
} from 'naive-ui'
import { computed, h, onMounted, ref } from 'vue'
import { CloudUploadRound } from '@vicons/material'
import { useUserStore } from '@/stores/user'
import type { SnFilePool } from '@/types/pool'
import { formatBytes } from './format'

import FilePoolSelect from '@/components/FilePoolSelect.vue'

import * as tus from 'tus-js-client'

const userStore = useUserStore()

const version = ref<any>(null)
async function fetchVersion() {
  const resp = await fetch('/api/version')
  version.value = await resp.json()
}
onMounted(() => fetchVersion())

type SnFilePoolOption = SnFilePool & any

const pools = ref<SnFilePoolOption[] | undefined>()
async function fetchPools() {
  const resp = await fetch('/api/pools')
  pools.value = await resp.json()
}
onMounted(() => fetchPools())

const renderSingleSelectTag: SelectRenderTag = ({ option }) => {
  return h(
    'div',
    {
      style: {
        display: 'flex',
        alignItems: 'center',
      },
    },
    [option.name as string],
  )
}

const perkPrivilegeList = ['Stellar', 'Nova', 'Supernova']

function renderPoolSelectLabel(option: SelectOption & SnFilePool) {
  const policy: any = option.policy_config
  return h(
    'div',
    {
      style: {
        padding: '8px 2px',
      },
    },
    [
      h('div', null, [option.name as string]),
      option.description &&
        h(
          'div',
          {
            style: {
              fontSize: '0.875rem',
              opacity: '0.75',
            },
          },
          option.description,
        ),
      h(
        'div',
        {
          style: {
            display: 'flex',
            marginBottom: '4px',
            fontSize: '0.75rem',
            opacity: '0.75',
          },
        },
        [
          policy.max_file_size && h('span', `Max ${formatBytes(policy.max_file_size)}`),
          policy.accept_types &&
            h(
              NTooltip,
              {},
              {
                trigger: () => h('span', `Accept limited types`),
                default: () => h('span', policy.accept_types.join(', ')),
              },
            ),
          policy.require_privilege &&
            h('span', `Require ${perkPrivilegeList[policy.require_privilege - 1]} Program`),
            h('span', `Cost x${option.billing_config.cost_multiplier.toFixed(1)} NSD`)
        ]
          .filter((el) => el)
          .flatMap((el, idx, arr) =>
            idx < arr.length - 1 ? [el, h(NDivider, { vertical: true })] : [el],
          ),
      ),
      h(
        'div',
        {
          style: {
            display: 'flex',
            gap: '0.25rem',
            marginTop: '2px',
            marginLeft: '-2px',
            marginRight: '-2px',
          },
        },
        [
          policy.public_usable &&
            h(
              NTag,
              {
                type: 'info',
                size: 'small',
                round: true,
              },
              { default: () => 'Public Shared' },
            ),
          policy.public_indexable &&
            h(
              NTag,
              {
                type: 'success',
                size: 'small',
                round: true,
              },
              { default: () => 'Public Indexable' },
            ),
          policy.allow_encryption &&
            h(
              NTag,
              {
                type: 'warning',
                size: 'small',
                round: true,
              },
              { default: () => 'Allow Encryption' },
            ),
          policy.allow_anonymous &&
            h(
              NTag,
              {
                type: 'info',
                size: 'small',
                round: true,
              },
              { default: () => 'Allow Anonymous' },
            ),
          policy.enable_recycle &&
            h(
              NTag,
              {
                type: 'info',
                size: 'small',
                round: true,
              },
              { default: () => 'Recycle Enabled' },
            ),
        ],
      ),
    ],
  )
}

const modeAdvanced = ref(false)

const filePool = ref<string | null>(null)
const filePass = ref<string>('')
const fileExpire = ref<number | null>(null)

const currentFilePool = computed(() => {
  if (!filePool.value) return null
  return pools.value?.find((pool) => pool.id === filePool.value) ?? null
})

const messageDisplay = useMessage()

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
  const requestHeaders: Record<string, string> = {}
  if (filePool.value) requestHeaders['X-FilePool'] = filePool.value
  if (filePass.value) requestHeaders['X-FilePass'] = filePass.value
  if (fileExpire.value) requestHeaders['X-FileExpire'] = fileExpire.value.toString()
  const upload = new tus.Upload(file.file, {
    endpoint: '/api/tus',
    retryDelays: [0, 3000, 5000, 10000, 20000],
    metadata: {
      filename: file.name,
      'content-type': file.type ?? 'application/octet-stream',
    },
    headers: {
      ...requestHeaders,
      ...headers,
    },
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
  const { url, name } = file
  if (!url) return
  window.open(url.replace('/api', ''), '_blank')
}

function customPreview(file: UploadFileInfo, detail: { event: MouseEvent }) {
  detail.event.preventDefault()
  const { url, type } = file
  if (!url) return
  window.open(url.replace('/api', ''), '_blank')
}

function disablePreviousDate(ts: number) {
  return ts <= Date.now()
}
</script>
