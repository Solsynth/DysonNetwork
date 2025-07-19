<script setup lang="ts">
import LayoutDefault from './layouts/default.vue'

import { RouterView } from 'vue-router'
import { NGlobalStyle, NConfigProvider } from 'naive-ui'
</script>

<template>
  <n-config-provider :theme-overrides="{ common: { fontFamily: 'Nunito Variable' } }">
    <n-global-style />
    <layout-default>
      <router-view />
    </layout-default>
  </n-config-provider>
</template>
