<template>
  <section class="h-full relative flex items-center justify-center">
    <n-card class="max-w-lg" title="Download file">
      <div class="flex flex-col gap-3" v-if="!progress">
        <n-input placeholder="File ID" v-model:value="fileId" />
        <n-input placeholder="Password" v-model:value="filePass" type="password" />
        <n-button @click="downloadFile">Download</n-button>
      </div>
      <div v-else>
        <n-progress :percentage="progress" />
      </div>
    </n-card>
  </section>
</template>

<script setup lang="ts">
import { NCard, NInput, NButton, NProgress, useMessage } from 'naive-ui'
import { ref } from 'vue'

import { downloadAndDecryptFile } from './secure'

const filePass = ref<string>('')
const fileId = ref<string>('')

const progress = ref<number | undefined>(0)

const messageDisplay = useMessage()

function downloadFile() {
  downloadAndDecryptFile('/api/files/' + fileId.value, filePass.value, (p: number) => {
    progress.value = p * 100
  }).catch((err) => {
    messageDisplay.error('Download failed: ' + err.message, { closable: true, duration: 10000 })
  })
}
</script>
