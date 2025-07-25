<script setup lang="ts">
import LayoutDefault from './layouts/default.vue'

import { RouterView } from 'vue-router'
import { NGlobalStyle, NConfigProvider, NMessageProvider, lightTheme, darkTheme } from 'naive-ui'
import { usePreferredDark } from '@vueuse/core'
import { useUserStore } from './stores/user'
import { onMounted } from 'vue'

const themeOverrides = {
  common: {
    fontFamily: 'Nunito Variable, v-sans, ui-system, -apple-system, sans-serif',
    primaryColor: '#7D80BAFF',
    primaryColorHover: '#9294C5FF',
    primaryColorPressed: '#575B9DFF',
    primaryColorSuppl: '#6B6FC1FF',
  },
}

const isDark = usePreferredDark()

const userStore = useUserStore()

onMounted(() => {
  userStore.fetchUser()
})
</script>

<template>
  <n-config-provider :theme-overrides="themeOverrides" :theme="isDark ? darkTheme : lightTheme">
    <n-global-style />
    <n-message-provider placement="bottom">
      <layout-default>
        <router-view />
      </layout-default>
    </n-message-provider>
  </n-config-provider>
</template>
