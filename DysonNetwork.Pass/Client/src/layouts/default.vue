<template>
  <n-layout>
    <n-layout-header class="border-b-1">Solar Network ID</n-layout-header>
    <n-layout-content embedded content-style="padding: 24px;">
      <slot />
    </n-layout-content>
  </n-layout>
</template>

<script lang="ts" setup>
import { NLayout, NLayoutHeader, NLayoutContent } from 'naive-ui'
</script>

<style scoped>
.n-layout-header,
.n-layout-footer {
  padding: 8px 24px;
  border-color: var(--n-border-color);
  text-align: center;
}

.n-layout-content {
  height: calc(100vh - 40px);
}
</style>
