<template>
  <div class="h-full max-w-5xl container mx-auto px-8">
    <n-grid cols="1 l:5" responsive="screen" :x-gap="16">
      <n-gi span="3">
        <n-infinite-scroll style="height: calc(100vh - 57px)" :distance="10" @load="fetchActivites">
          <div v-for="activity in activites" :key="activity.id" class="mt-4">
            <post-item v-if="activity.type == 'posts.new'" :item="activity.data" />
          </div>
        </n-infinite-scroll>
      </n-gi>
      <n-gi span="2" class="max-lg:order-first">
        <n-card class="w-full mt-4" title="About">
          <p>Welcome to the <b>Solar Network</b></p>
          <p>The open social network. Friendly to everyone.</p>

          <p class="mt-4 opacity-75 text-xs">
            <span v-if="version == null">Loading...</span>
            <span v-else>
              v{{ version.version }} @
              {{ version.commit.substring(0, 6) }}
              {{ version.updatedAt }}
            </span>
          </p>
        </n-card>
      </n-gi>
    </n-grid>
  </div>
</template>

<script setup lang="ts">
import { NCard, NInfiniteScroll, NGrid, NGi } from 'naive-ui'
import { computed, onMounted, ref } from 'vue'
import { useUserStore } from '@/stores/user'

import PostItem from '@/components/PostItem.vue'

const userStore = useUserStore()

const version = ref<any>(null)
async function fetchVersion() {
  const resp = await fetch('/api/version')
  version.value = await resp.json()
}
onMounted(() => fetchVersion())

const loading = ref(false)

const activites = ref<any[]>([])
const activitesLast = computed(
  () =>
    activites.value.sort(
      (a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime(),
    )[0],
)

async function fetchActivites() {
  loading.value = true
  const resp = await fetch(
    activitesLast.value == null
      ? '/api/activities'
      : `/api/activities?cursor=${new Date(activitesLast.value.created_at).toISOString()}`,
  )
  activites.value.push(...(await resp.json()))
  loading.value = false
}
onMounted(() => fetchActivites())
</script>
