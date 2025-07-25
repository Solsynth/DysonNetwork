<template>
  <section class="h-full relative flex items-center justify-center">
    <n-card class="max-w-lg" title="About">
      <p>Welcome to the <b>Solar Drive</b></p>
      <p>
        We help you upload, collect, and share files with ease in mind.
      </p>

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
import { NCard } from 'naive-ui'
import { onMounted, ref } from 'vue'

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
