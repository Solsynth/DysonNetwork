<template>
  <div class="flex gap-3 items-center">
    <n-avatar round :size="40" :src="publisherAvatar" />
    <div class="flex-grow-1 flex flex-col">
      <p class="flex gap-1 items-baseline">
        <span class="font-bold">{{ props.item.publisher.nick }}</span>
        <span class="text-xs">@{{ props.item.publisher.name }}</span>
      </p>
      <p class="text-xs flex gap-1">
        <span>{{ dayjs(props.item.created_at).utc().fromNow() }}</span>
        <span class="font-bold">·</span>
        <span>{{ new Date(props.item.created_at).toLocaleString() }}</span>
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { NAvatar } from 'naive-ui'
import { computed } from 'vue'

import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import utc from 'dayjs/plugin/utc'

dayjs.extend(utc)
dayjs.extend(relativeTime)

const props = defineProps<{ item: any }>()

const publisherAvatar = computed(() =>
  props.item.publisher.picture ? `/cgi/drive/files/${props.item.publisher.picture.id}` : undefined,
)
</script>
