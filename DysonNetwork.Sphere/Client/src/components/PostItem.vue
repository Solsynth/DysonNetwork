<template>
  <n-card>
    <div class="flex flex-col gap-3">
      <post-header :item="props.item" />

      <div v-if="props.item.title || props.item.description">
        <h2 class="text-lg" v-if="props.item.title">{{ props.item.title }}</h2>
        <p class="text-sm" v-if="props.item.description">
          {{ props.item.description }}
        </p>
      </div>

      <article v-if="htmlContent" class="prose prose-sm dark:prose-invert prose-slate prose-p:m-0">
        <div v-html="htmlContent"></div>
      </article>

      <div v-if="props.item.attachments">
        <n-image-group>
          <n-space>
            <attachment-item
              v-for="attachment in props.item.attachments"
              :key="attachment.id"
              :item="attachment"
            />
          </n-space>
        </n-image-group>
      </div>
    </div>
  </n-card>
</template>

<script lang="ts" setup>
import { NCard, NImageGroup, NSpace } from 'naive-ui'
import { ref, watch } from 'vue'
import { Marked } from 'marked'

import PostHeader from './PostHeader.vue'
import AttachmentItem from './AttachmentItem.vue'

const props = defineProps<{ item: any }>()

const marked = new Marked()

const htmlContent = ref<string>('')

watch(
  props.item,
  async (value) => {
    if (value.content) htmlContent.value = await marked.parse(value.content)
  },
  { immediate: true, deep: true },
)
</script>
