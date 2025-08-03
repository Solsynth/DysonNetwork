<template>
  <n-image v-if="itemType == 'image'" :src="remoteSource" class="rounded-md">
    <template #error>
      <img src="/image-broken.jpg" class="w-32 h-32 rounded-md" />
    </template>
  </n-image>
</template>

<script lang="ts" setup>
import { NImage } from 'naive-ui'
import { computed } from 'vue'

const props = defineProps<{ item: any }>()

const itemType = computed(() => props.item.mime_type.split('/')[0] ?? 'unknown')

const remoteSource = computed(() => `/cgi/drive/files/${props.item.id}`)
</script>
