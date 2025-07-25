<template>
  <n-layout>
    <n-layout-header class="border-b-1 flex justify-between items-center">
      <router-link to="/" class="text-lg font-bold">Solar Network ID</router-link>
      <div v-if="!hideUserMenu">
        <n-dropdown
          v-if="!userStore.isAuthenticated"
          :options="guestOptions"
          @select="handleGuestMenuSelect"
        >
          <n-button>Account</n-button>
        </n-dropdown>
        <n-dropdown v-else :options="userOptions" @select="handleUserMenuSelect" type="primary">
          <n-button>{{ userStore.user.nick }}</n-button>
        </n-dropdown>
      </div>
    </n-layout-header>
    <n-layout-content embedded content-style="padding: 24px;">
      <router-view />
    </n-layout-content>
  </n-layout>
</template>

<script lang="ts" setup>
import { computed, h } from 'vue'
import { NLayout, NLayoutHeader, NLayoutContent, NButton, NDropdown, NIcon } from 'naive-ui'
import {
  LogInOutlined,
  PersonAddAlt1Outlined,
  LogOutOutlined,
  PersonOutlineRound,
} from '@vicons/material'
import { useUserStore } from '@/stores/user'
import { useRoute, useRouter } from 'vue-router'

const userStore = useUserStore()
const route = useRoute()
const router = useRouter()

const hideUserMenu = computed(() => {
  return ['captcha', 'spells', 'login', 'create-account'].includes(route.name as string)
})

const guestOptions = [
  {
    label: 'Login',
    key: 'login',
    icon: () =>
      h(NIcon, null, {
        default: () => h(LogInOutlined),
      }),
  },
  {
    label: 'Create Account',
    key: 'create-account',
    icon: () =>
      h(NIcon, null, {
        default: () => h(PersonAddAlt1Outlined),
      }),
  },
]

const userOptions = computed(() => [
  {
    label: 'Profile',
    key: 'profile',
    icon: () =>
      h(NIcon, null, {
        default: () => h(PersonOutlineRound),
      }),
  },
  {
    label: 'Logout',
    key: 'logout',
    icon: () =>
      h(NIcon, null, {
        default: () => h(LogOutOutlined),
      }),
  },
])

function handleGuestMenuSelect(key: string) {
  if (key === 'login') {
    router.push('/login')
  } else if (key === 'create-account') {
    router.push('/create-account')
  }
}

function handleUserMenuSelect(key: string) {
  if (key === 'logout') {
    userStore.logout()
    router.push('/login')
  } else if (key === 'profile') {
    router.push('/accounts/me') // Assuming you have a profile page
  }
}
</script>

<style scoped>
.n-layout-header {
  padding: 8px 24px;
  border-color: var(--n-border-color);
  height: 57px; /* Fixed height */
  display: flex;
  align-items: center;
}

.n-layout-content {
  height: calc(100vh - 57px); /* Adjust based on header height */
}
</style>
