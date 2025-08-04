<template>
  <div v-if="user">
    <img
      :src="userBackground"
      class="object-cover w-full max-h-48 mb-8"
      style="aspect-ratio: 16/7"
    />

    <div class="container mx-auto px-8">
      <div class="flex items-center gap-6 mb-8">
        <n-avatar round :size="100" :alt="user.name" :src="userPicture">
          <n-icon size="48" v-if="!userPicture">
            <person-round />
          </n-icon>
        </n-avatar>
        <div>
          <n-text strong class="text-2xl">
            {{ user.nick || user.name }}
          </n-text>
          <n-text depth="3" class="block">@{{ user.name }}</n-text>
        </div>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div class="flex flex-col gap-4">
          <n-card title="Info">
            <div class="flex gap-2" v-if="user.profile.location">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <access-time-outlined />
                </n-icon>
                Time Zone
              </span>
              <span class="flex gap-2">
                <span>
                  {{ new Date().toLocaleTimeString(void 0, { timeZone: user.profile.time_zone }) }}
                </span>
                <span class="font-bold">·</span>
                <span>{{ getOffsetUTCString(user.profile.time_zone) }}</span>
                <span class="font-bold">·</span>
                <span>{{ user.profile.time_zone }}</span>
              </span>
            </div>
            <div class="flex gap-2" v-if="user.profile.location">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <location-on-outlined />
                </n-icon>
                Location
              </span>
              <span>
                {{ user.profile.location }}
              </span>
            </div>
            <div class="flex gap-2" v-if="user.profile.first_name || user.profile.last_name">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <drive-file-rename-outline-outlined />
                </n-icon>
                Name
              </span>
              <span>
                {{
                  [user.profile.first_name, user.profile.middle_name, user.profile.last_name].join(
                    ' ',
                  )
                }}
              </span>
            </div>
            <div class="flex gap-2" v-if="user.profile.gender || user.profile.pronouns">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <person-round />
                </n-icon>
                Gender
              </span>
              <span class="flex gap-2">
                <span>{{ user.profile.gender || 'Unspecified' }}</span>
                <span class="font-bold">·</span>
                <span>{{ user.profile.pronouns || 'Unspeificed' }}</span>
              </span>
            </div>
            <div class="flex gap-2">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <calendar-month-outlined />
                </n-icon>
                Joined at
              </span>
              <span>{{ new Date(user.created_at).toLocaleDateString() }}</span>
            </div>
            <div class="flex gap-2" v-if="user.profile.birthday">
              <span class="flex-grow-1 flex items-center gap-2">
                <n-icon>
                  <cake-outlined />
                </n-icon>
                Birthday
              </span>
              <span class="flex gap-2">
                <span>{{ calculateAge(new Date(user.profile.birthday)) }} yrs old</span>
                <span class="font-bold">·</span>
                <span>{{ new Date(user.profile.birthday).toLocaleDateString() }}</span>
              </span>
            </div>
          </n-card>
          <n-card v-if="user.perk_subscription">
            <div class="flex justify-between items-center">
              <div class="flex flex-col">
                <n-text class="font-bold text-xl">
                  {{ perkSubscriptionNames[user.perk_subscription.identifier].name }}
                  Tier
                </n-text>
                <n-text>Stellar Program Member</n-text>
              </div>
              <n-icon
                :size="48"
                :color="perkSubscriptionNames[user.perk_subscription.identifier].color"
              >
                <star-round />
              </n-icon>
            </div>
          </n-card>
          <n-card>
            <div class="flex justify-between mb-2">
              <n-text>Level {{ user.profile.level }}</n-text>
              <n-text>{{ user.profile.experience }} XP</n-text>
            </div>
            <n-progress
              type="line"
              :percentage="user.profile.leveling_progress"
              :height="8"
              status="success"
              :show-indicator="false"
            />
          </n-card>
        </div>
        <div>
          <n-card v-if="htmlBio" title="Bio">
            <article
              class="bio-prose prose dark:prose-invert prose-slate"
              v-html="htmlBio"
            ></article>
          </n-card>
        </div>
      </div>
    </div>
  </div>
  <div v-else-if="notFound" class="flex justify-center items-center h-full">
    <n-result
      status="404"
      title="User not found"
      description="The user profile you're trying to access is not found."
    />
  </div>
  <div v-else class="flex justify-center items-center h-full">
    <n-spin />
  </div>
</template>

<script setup lang="ts">
import { NResult, NSpin, NCard, NAvatar, NText, NProgress, NIcon } from 'naive-ui'
import {
  PersonRound,
  CalendarMonthOutlined,
  CakeOutlined,
  DriveFileRenameOutlineOutlined,
  LocationOnOutlined,
  AccessTimeOutlined,
  StarRound,
} from '@vicons/material'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Marked } from 'marked'

const route = useRoute()

const notFound = ref<boolean>(false)
const user = ref<any>(null)

async function fetchUser() {
  if (window.DyPrefetch?.Account != null) {
    console.log('[Fetch] Use the pre-rendered account data.')
    user.value = window.DyPrefetch.Account
    return
  }

  console.log('[Fetch] Using the API to load user data.')
  try {
    const resp = await fetch(`/api/accounts/${route.params.name}`)
    user.value = await resp.json()
  } catch (err) {
    console.error(err)
    notFound.value = true
  }
}

onMounted(() => fetchUser())

interface PerkSubscriptionInfo {
  name: string
  tier: number
  color: string
}

const perkSubscriptionNames: Record<string, PerkSubscriptionInfo> = {
  'solian.stellar.primary': {
    name: 'Stellar',
    tier: 1,
    color: '#2196f3',
  },
  'solian.stellar.nova': {
    name: 'Nova',
    tier: 2,
    color: '#39c5bb',
  },
  'solian.stellar.supernova': {
    name: 'Supernova',
    tier: 3,
    color: '#ffc109',
  },
}

const marked = new Marked()

const htmlBio = ref<string | undefined>(undefined)

watch(user, async (value) => {
  htmlBio.value = value?.profile.bio ? await marked.parse(value.profile.bio) : undefined
})

const userBackground = computed(() => {
  return user.value?.profile.background
    ? `/cgi/drive/files/${user.value.profile.background.id}?original=true`
    : undefined
})
const userPicture = computed(() => {
  return user.value?.profile.picture
    ? `/cgi/drive/files/${user.value.profile.picture.id}`
    : undefined
})

function calculateAge(birthday: Date) {
  const birthDate = new Date(birthday)
  const today = new Date()

  let age = today.getFullYear() - birthDate.getFullYear()

  // Check if the birthday hasn't occurred yet this year
  const monthDiff = today.getMonth() - birthDate.getMonth()
  const dayDiff = today.getDate() - birthDate.getDate()

  if (monthDiff < 0 || (monthDiff === 0 && dayDiff < 0)) {
    age--
  }

  return age
}

function getOffsetUTCString(targetTimeZone: string): string {
  const now = new Date()

  const localOffset = now.getTimezoneOffset() // in minutes
  const targetTime = new Date(now.toLocaleString('en-US', { timeZone: targetTimeZone }))
  const targetOffset = (now.getTime() - targetTime.getTime()) / 60000

  const diff = targetOffset - localOffset

  const sign = diff <= 0 ? '+' : '-' // inverted because positive offset is west of UTC
  const abs = Math.abs(diff)
  const hours = String(Math.floor(abs / 60)).padStart(2, '0')
  const minutes = String(Math.floor(abs % 60)).padStart(2, '0')

  return `${sign}${hours}:${minutes}`
}
</script>

<style>
.bio-prose img {
  display: inline !important;
  margin: 0 !important;
}
</style>
