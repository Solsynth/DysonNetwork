<template>
  <section class="h-full flex items-center justify-center">
    <n-result status="404" title="404" description="Page not found">
      <template #footer>
        <n-button @click="router.push('/')">Go to Home</n-button>
      </template>
    </n-result>
  </section>
</template>

<script lang="ts" setup>
import { NResult, NButton } from 'naive-ui'
import { useRouter } from 'vue-router';

const router = useRouter()
</script>
