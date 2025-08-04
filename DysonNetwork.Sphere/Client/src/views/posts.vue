<template>
  <div v-if="post" class="container max-w-5xl mx-auto mt-4">
    <n-grid cols="1 l:5" responsive="screen" :x-gap="16">
      <n-gi span="3">
        <post-item :item="post" />
      </n-gi>
      <n-gi span="2">
        <n-card title="About the author">
          <div class="relative mb-7">
            <img
              class="object-cover rounded-lg"
              style="aspect-ratio: 16/7"
              :src="publisherBackground"
            />
            <div class="absolute left-3 bottom-[-24px]">
              <n-avatar :src="publisherAvatar" :size="64" round bordered />
            </div>
          </div>
          <div class="flex flex-col">
            <p class="flex gap-1 items-baseline">
              <span class="font-bold">
                {{ post.publisher.nick }}
              </span>
              <span class="text-sm"> @{{ post.publisher.name }} </span>
            </p>
            <div class="max-h-96 overflow-y-auto">
              <div
                class="prose prose-sm dark:prose-invert prose-slate"
                v-if="publisherBio"
                v-html="publisherBio"
              ></div>
            </div>
          </div>
        </n-card>
      </n-gi>
    </n-grid>
  </div>
  <div v-else-if="notFound" class="flex justify-center items-center h-full">
    <n-result
      status="404"
      title="Post not found"
      description="The post you are looking cannot be found, it might be deleted, or you have no permission to view it or it just never been posted."
    />
  </div>
  <div v-else class="flex justify-center items-center h-full">
    <n-spin />
  </div>
</template>

<script setup lang="ts">
import { NGrid, NGi, NCard, NAvatar } from 'naive-ui'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Marked } from 'marked'

import PostItem from '@/components/PostItem.vue'

const route = useRoute()

const post = ref<any>()
const notFound = ref(false)

async function fetchPost() {
  if (window.DyPrefetch?.Post != null) {
    console.log('[Fetch] Use the pre-rendered post data.')
    post.value = window.DyPrefetch.post
    return
  }

  console.log('[Fetch] Using the API to load user data.')
  try {
    const resp = await fetch(`/api/posts/${route.params.slug}`)
    post.value = await resp.json()
  } catch (err) {
    console.error(err)
    notFound.value = true
  }
}
onMounted(() => fetchPost())

const publisherAvatar = computed(() =>
  post.value.publisher.picture ? `/cgi/drive/files/${post.value.publisher.picture.id}` : undefined,
)
const publisherBackground = computed(() =>
  post.value.publisher.background
    ? `/cgi/drive/files/${post.value.publisher.background.id}`
    : undefined,
)

const marked = new Marked()

const publisherBio = ref('')
watch(
  post,
  async (value) => {
    if (value?.publisher?.bio) publisherBio.value = await marked.parse(value.publisher.bio)
  },
  { immediate: true, deep: true },
)
</script>
