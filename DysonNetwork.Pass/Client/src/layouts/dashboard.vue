<template>
  <div>
    <img :src="userBackground" class="object-cover w-full max-h-48" style="aspect-ratio: 16/7" />

    <n-tabs
      animated
      justify-content="center"
      type="line"
      placement="top"
      :value="route.name?.toString()"
      @update:value="onSwitchTab"
    >
      <n-tab-pane name="dashboardCurrent" tab="Information">
        <router-view />
      </n-tab-pane>
      <n-tab-pane name="dashboardSecurity" tab="Security">
        <router-view />
      </n-tab-pane>
    </n-tabs>
  </div>
</template>

<script setup lang="ts">
import { useUserStore } from '@/stores/user'
import { NTabs, NTabPane } from 'naive-ui'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const userStore = useUserStore()

function onSwitchTab(name: string) {
  router.push({ name })
}

const userBackground = computed(() => {
  return userStore.user.profile.background
    ? `/cgi/drive/files/${userStore.user.profile.background.id}?original=true`
    : undefined
})
</script>
