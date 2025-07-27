<template>
  <section class="h-full relative flex flex-col items-center justify-center">
    <n-card class="max-w-lg my-4 mx-8" title="About" v-if="!userStore.user">
      <p>Welcome to the <b>Solar Drive</b></p>
      <p>We help you upload, collect, and share files with ease in mind.</p>
      <p>To continue, login first.</p>
    </n-card>

    <n-card class="max-w-2xl" v-else content-style="padding: 0;">
      <n-tabs type="line" animated :tabs-padding="20" pane-style="padding: 20px">
        <template #suffix>
          <div class="flex gap-2 items-center me-4">
            <p>Advance Mode</p>
            <n-switch v-model:value="modeAdvanced" size="small" />
          </div>
        </template>

        <n-tab-pane name="direct" tab="Direct Upload" :disabled="isBundleMode">
          <div class="mb-3">
            <file-pool-select v-model="filePool" @update:pool="currentFilePool = $event" />
          </div>
          <upload-area
            :filePool="filePool"
            :pools="pools as SnFilePool[]"
            :modeAdvanced="modeAdvanced"
          />
        </n-tab-pane>
        <n-tab-pane name="bundle" tab="Bundle Upload">
          <div class="mb-3">
            <bundle-select v-model:bundle="selectedBundleId" :disabled="isBundleMode" />
          </div>

          <n-modal v-model:show="showCreateBundleModal" preset="dialog" title="Create New Bundle">
            <bundle-form ref="bundleFormRef" :value="newBundle" />
            <template #action>
              <n-button @click="showCreateBundleModal = false">Cancel</n-button>
              <n-button type="primary" @click="createBundle">Create</n-button>
            </template>
          </n-modal>

          <div class="flex justify-between">
            <n-button @click="showCreateBundleModal = true" class="mb-3" :disabled="isBundleMode">
              Create New Bundle
            </n-button>
            <n-button
              type="primary"
              :disabled="!selectedBundleId && !newBundleId && !isBundleMode"
              @click="isBundleMode ? cancelBundleUpload() : proceedToBundleUpload()"
            >
              {{ isBundleMode ? 'Cancel' : 'Proceed to Upload' }}
            </n-button>
          </div>

          <div v-if="bundleUploadMode" class="mt-3">
            <div class="mb-3">
              <file-pool-select v-model="filePool" @update:pool="currentFilePool = $event" />
            </div>
            <upload-area
              :filePool="filePool"
              :pools="pools as SnFilePool[]"
              :modeAdvanced="modeAdvanced"
              :bundleId="currentBundleId!"
            />
          </div>
        </n-tab-pane>
      </n-tabs>
    </n-card>

    <p class="mt-4 opacity-75 text-xs">
      <span v-if="version == null">Loading...</span>
      <span v-else>
        v{{ version.version }} @
        {{ version.commit.substring(0, 6) }}
        {{ version.updatedAt }}
      </span>
    </p>
  </section>
</template>

<script setup lang="ts">
import { NCard, NSwitch, NTabs, NTabPane, NButton, NModal } from 'naive-ui'
import { computed, onMounted, ref } from 'vue'
import { useUserStore } from '@/stores/user'
import type { SnFilePool } from '@/types/pool'
import FilePoolSelect from '@/components/FilePoolSelect.vue'
import UploadArea from '@/components/UploadArea.vue'
import BundleSelect from '@/components/BundleSelect.vue'
import BundleForm from '@/components/form/BundleForm.vue'

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

const modeAdvanced = ref(false)

const filePool = ref<string | null>(null)

const currentFilePool = computed(() => {
  if (!filePool.value) return null
  return pools.value?.find((pool) => pool.id === filePool.value) ?? null
})

const bundles = ref<any>([])
const selectedBundleId = ref<string | null>(null)
const showCreateBundleModal = ref(false)
const newBundle = ref<any>({})
const bundleFormRef = ref<any>(null)
const bundleUploadMode = ref(false)
const currentBundleId = ref<string | null>(null)
const newBundleId = ref<string | null>(null)
const isBundleMode = ref(false)

async function createBundle() {
  try {
    await bundleFormRef.value?.formRef?.validate()
    const resp = await fetch('/api/bundles', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(newBundle.value),
    })
    if (!resp.ok) {
      throw new Error('Failed to create bundle')
    }
    const createdBundle = await resp.json()
    bundles.value.push(createdBundle)
    selectedBundleId.value = createdBundle.id
    newBundleId.value = createdBundle.id
    showCreateBundleModal.value = false
    newBundle.value = {}
  } catch (error) {
    console.error('Failed to create bundle:', error)
  }
}

function proceedToBundleUpload() {
  currentBundleId.value = selectedBundleId.value || newBundleId.value
  bundleUploadMode.value = true
  isBundleMode.value = true
}

function cancelBundleUpload() {
  bundleUploadMode.value = false
  isBundleMode.value = false
  currentBundleId.value = null
  selectedBundleId.value = null
  newBundleId.value = null
}
</script>
