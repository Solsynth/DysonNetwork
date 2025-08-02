<template>
  <div class="container mx-auto px-8">
    <div class="flex items-center gap-6 mb-8 mt-4">
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

    <div class="mt-8">
      <n-h3>Update Profile</n-h3>
      <n-alert type="info" title="Under Construction">
        Update profile directly on the Solar Network ID site is not available, yet. You can still do
        it inside the Solian app or web app.
      </n-alert>
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
import { NAvatar, NText, NAlert, NH3, NButton, NIcon } from 'naive-ui'
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
