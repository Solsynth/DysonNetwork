<template>
  <div class="h-full max-w-5xl container mx-auto px-8">
    <n-grid cols="1 l:5" responsive="screen" :x-gap="16">
      <n-gi span="3">
        <n-infinite-scroll style="height: calc(100vh - 57px)" :distance="10" @load="fetchActivites">
          <div v-for="activity in activites" :key="activity.id" class="mt-4">
            <post-item
              v-if="activity.type.startsWith('posts')"
              :item="activity.data"
              @click="router.push('/posts/' + activity.id)"
            />
          </div>
        </n-infinite-scroll>
      </n-gi>
      <n-gi span="2" class="max-lg:order-first">
        <n-card class="w-full mt-4" title="About" v-if="!userStore.user">
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
        <n-card class="mt-4 w-full">
          <post-editor @posted="refreshActivities" />
        </n-card>
        <n-alert closable class="mt-4" w-full type="info" title="Looking for Solian?">
          The flutter based web app Solian has been moved to
          <n-a href="https://web.solian.app" target="_blank">web.solian.app</n-a>
          <n-hr />
          网页版 Solian 已经被移动到
          <n-a href="https://web.solian.app" target="_blank">web.solian.app</n-a>
        </n-alert>
      </n-gi>
    </n-grid>
  </div>
</template>

<script setup lang="ts">
import { NCard, NInfiniteScroll, NGrid, NGi, NAlert, NA, NHr } from 'naive-ui'
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useUserStore } from '@/stores/user'

import PostEditor from '@/components/PostEditor.vue'
import PostItem from '@/components/PostItem.vue'

const router = useRouter()

const userStore = useUserStore()

const version = ref<any>(null)
async function fetchVersion() {
  const resp = await fetch('/api/version')
  version.value = await resp.json()
}
onMounted(() => fetchVersion())

const loading = ref(false)

const activites = ref<any[]>([])
const activitesLast = computed(() => activites.value[Math.max(activites.value.length - 1, 0)])
const activitesHasMore = ref(true)

async function fetchActivites() {
  if (loading.value) return
  if (!activitesHasMore.value) return
  loading.value = true
  const resp = await fetch(
    activitesLast.value == null
      ? '/api/activities'
      : `/api/activities?cursor=${new Date(activitesLast.value.created_at).toISOString()}`,
  )
  const data = await resp.json()
  activites.value = [...activites.value, ...data]
  activitesHasMore.value = data[0]?.type != 'empty'
  loading.value = false
}
onMounted(() => fetchActivites())

async function refreshActivities() {
  activites.value = []
  fetchActivites()
}
</script>
