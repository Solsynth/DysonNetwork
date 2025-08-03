<template>
  <div class="flex flex-col gap-3">
    <pub-select v-model:value="publisher" />
    <n-input
      type="textarea"
      placeholder="What's happended?!"
      v-model:value="content"
      @keydown.meta.enter.exact="submit"
      @keydown.ctrl.enter.exact="submit"
    />
    <div class="flex justify-between">
      <div class="flex gap-2"></div>
      <n-button type="primary" icon-placement="right" :loading="submitting" @click="submit">
        Post
        <template #icon>
          <n-icon><send-round /></n-icon>
        </template>
      </n-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { NInput, NButton, NIcon } from 'naive-ui'
import { ref } from 'vue'

import { SendRound } from '@vicons/material'

import PubSelect from './PubSelect.vue'

const emits = defineEmits(['posted'])

const publisher = ref<string | undefined>()
const content = ref('')

const submitting = ref(false)

async function submit() {
  submitting.value = true
  await fetch(`/api/posts?pub=${publisher.value}`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
    },
    body: JSON.stringify({
      content: content.value,
    }),
  })

  submitting.value = false
  content.value = ''
  emits('posted')
}
</script>
