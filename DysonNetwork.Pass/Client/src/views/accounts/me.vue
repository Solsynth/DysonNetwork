<template>
  <div class="max-w-3xl mx-auto p-8">
    <div class="flex items-center gap-6 mb-8">
      <n-avatar round :size="100" :alt="userStore.user.name" :src="userPicture">
        <n-icon size="48" v-if="!userPicture">
          <person-round />
        </n-icon>
      </n-avatar>
      <div>
        <n-text strong class="text-2xl">
          {{ userStore.user.nick || userStore.user.name }}
        </n-text>
        <n-text depth="3" class="block">@{{ userStore.user.name }}</n-text>
      </div>
    </div>

    <div class="mb-8">
      <div class="flex justify-between mb-2">
        <n-text>Level {{ userStore.user.profile.level }}</n-text>
        <n-text>{{ userStore.user.profile.experience }} XP</n-text>
      </div>
      <n-progress
        type="line"
        :percentage="userStore.user.profile.leveling_progress"
        :height="8"
        status="success"
        :show-indicator="false"
      />
    </div>

    <div v-if="userStore.user.profile.bio" class="mt-8">
      <n-h3>About</n-h3>
      <n-p>{{ userStore.user.profile.bio }}</n-p>
    </div>

    <div class="mt-8">
      <n-button type="primary" icon-placement="right" tag="a" href="https://solian.app/#/account">
        Open in the Solian
        <template #icon>
          <n-icon>
            <launch-outlined />
          </n-icon>
        </template>
      </n-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { NAvatar, NText, NProgress, NH3, NP, NButton, NIcon } from 'naive-ui'
import { PersonRound, LaunchOutlined } from '@vicons/material'
import { useUserStore } from '@/stores/user'
import { computed } from 'vue'

const userStore = useUserStore()

const userPicture = computed(() => {
  return userStore.user.profile.picture
    ? `/cgi/drive/files/${userStore.user.profile.picture.id}`
    : undefined
})
</script>
