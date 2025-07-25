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
    <n-card class="max-w-2xl" v-else>
      <dashboard
        :uppy="uppy"
        :props="{ theme: 'auto', height: '28rem', proudlyDisplayPoweredByUppy: false }"
      />
    </n-card>
  </section>
</template>

<script setup lang="ts">
import { useUserStore } from '@/stores/user'
import { NCard } from 'naive-ui'
import { onMounted, ref } from 'vue'
import { Dashboard } from '@uppy/vue'

import Uppy from '@uppy/core'
import Tus from '@uppy/tus'

import '@uppy/core/dist/style.min.css'
import '@uppy/dashboard/dist/style.min.css'

const uppy = new Uppy()
uppy.use(Tus, { endpoint: '/api/tus' })

const userStore = useUserStore()

const version = ref<any>(null)

async function fetchVersion() {
  const resp = await fetch('/api/version')
  version.value = await resp.json()
}

onMounted(() => fetchVersion())
</script>

<style scoped>
/* Add any specific styles here if needed, though Tailwind should handle most. */
</style>
