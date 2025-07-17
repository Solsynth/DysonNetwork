<template>
  <div class="h-full flex items-center justify-center">
    <n-card class="max-w-lg" title="Spell">
      <n-alert type="success" v-if="done">
        The magic spell has been applied successfully. Now you can close this tab and back to the
        Solar Network!
      </n-alert>
      <n-alert type="error" v-else-if="!!error" title="Something went wrong">{{ error }}</n-alert>
      <div v-else-if="!!spell">
        <p class="mb-2">Magic spell for {{ spellTypes[spell.type] ?? 'unknown' }}</p>
        <div class="flex items-center gap-1">
          <n-icon size="18"><account-circle-outlined /></n-icon>
          <b>@{{ spell.account.name }}</b>
        </div>
        <div class="flex items-center gap-1">
          <n-icon size="18"><play-arrow-filled /></n-icon>
          <span>Available at</span>
          <b>{{ new Date(spell.created_at ?? spell.affected_at).toLocaleString() }}</b>
        </div>
        <div class="flex items-center gap-1" v-if="spell.expired_at">
          <n-icon size="18"><date-range-filled /></n-icon>
          <span>Until</span>
          <b>{{ spell.expired_at.toString() }}</b>
        </div>
        <div class="mt-4">
          <n-input v-if="spell.type == 3" v-model:value="newPassword" />
          <n-button type="primary" :loading="submitting" @click="applySpell">
            <template #icon><check-filled /></template>
            Apply
          </n-button>
        </div>
      </div>
      <n-spin v-else size="small" />
    </n-card>
  </div>
</template>

<script setup lang="ts">
import { NCard, NAlert, NSpin, NIcon, NButton, NInput } from 'naive-ui'
import {
  AccountCircleOutlined,
  PlayArrowFilled,
  DateRangeFilled,
  CheckFilled,
} from '@vicons/material'
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()

const spellWord: string = route.params.word.toString()
const spell = ref<any>(null)
const error = ref<string | null>(null)

const newPassword = ref<string>()

const submitting = ref(false)
const done = ref(false)

const spellTypes = [
  'Account Acivation',
  'Account Deactivation',
  'Account Deletion',
  'Reset Password',
  'Contact Method Verification',
]

async function fetchSpell() {
  // @ts-ignore
  if (window.__APP_DATA__ != null) {
    // @ts-ignore
    spell.value = window.__APP_DATA__['Spell']
    return
  }
  const resp = await fetch(`/api/spells/${encodeURIComponent(spellWord)}`)
  if (resp.status === 200) {
    const data = await resp.json()
    spell.value = data
  } else {
    error.value = await resp.text()
  }
}

async function applySpell() {
  submitting.value = true
  const resp = await fetch(`/api/spells/${encodeURIComponent(spellWord)}/apply`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: newPassword.value ? JSON.stringify({ new_password: newPassword.value }) : null,
  })
  if (resp.status === 200) {
    done.value = true
  } else {
    error.value = await resp.text()
  }
}

onMounted(() => fetchSpell())
</script>
