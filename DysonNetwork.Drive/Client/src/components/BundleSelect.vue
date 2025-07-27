<template>
  <n-select
    v-model:value="selectedBundle"
    :options="options"
    placeholder="Select a bundle"
    @update:value="handleBundleChange"
    filterable
    remote
    :loading="loading"
    @search="handleSearch"
    clearable
  />
</template>

<script setup lang="ts">
import { NSelect } from 'naive-ui'
import { ref, onMounted } from 'vue'

const emit = defineEmits(['update:bundle'])

const selectedBundle = ref<string | null>(null)
const loading = ref(false)
const options = ref<any[]>([])

async function fetchBundles(term: string | null = null) {
  loading.value = true
  try {
    const resp = await fetch(`/api/bundles/me?${term ? `term=${term}` : ''}`)
    const data = await resp.json()
    options.value = data.map((bundle: any) => ({
      label: bundle.name,
      value: bundle.id,
    }))
  } catch (error) {
    console.error('Failed to fetch bundles:', error)
  } finally {
    loading.value = false
  }
}

function handleSearch(query: string) {
  fetchBundles(query)
}

function handleBundleChange(value: string) {
  emit('update:bundle', value)
}

onMounted(() => fetchBundles())
</script>
