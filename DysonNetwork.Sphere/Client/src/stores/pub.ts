import { defineStore } from 'pinia'
import { ref } from 'vue'

export const usePubStore = defineStore('pub', () => {
  const publishers = ref<any[]>([])

  async function fetchPublishers() {
    const resp = await fetch('/api/publishers')
    publishers.value = await resp.json()
  }

  return { publishers, fetchPublishers }
})
