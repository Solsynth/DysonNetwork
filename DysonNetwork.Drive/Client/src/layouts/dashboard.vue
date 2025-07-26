<template>
  <n-layout has-sider class="h-full">
    <n-layout-sider bordered collapse-mode="width" :collapsed-width="64" :width="240" show-trigger>
      <n-menu
        :collapsed-width="64"
        :collapsed-icon-size="22"
        :options="menuOptions"
        :value="route.name as string"
        @update:value="updateMenuSelect"
      />
    </n-layout-sider>
    <n-layout>
      <router-view />
    </n-layout>
  </n-layout>
</template>

<script setup lang="ts">
import { DataUsageRound, AllInboxFilled } from '@vicons/material'
import { NIcon, NLayout, NLayoutSider, NMenu, type MenuOption } from 'naive-ui'
import { h, type Component } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

function renderIcon(icon: Component) {
  return () => h(NIcon, null, { default: () => h(icon) })
}

const menuOptions: MenuOption[] = [
  {
    label: 'Usage',
    key: 'dashboardUsage',
    icon: renderIcon(DataUsageRound),
  },
  {
    label: 'Files',
    key: 'dashboardFiles',
    icon: renderIcon(AllInboxFilled),
  }
]

function updateMenuSelect(key: string) {
  router.push({ name: key })
}
</script>
