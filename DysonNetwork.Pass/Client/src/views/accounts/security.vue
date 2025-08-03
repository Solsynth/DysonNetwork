<template>
  <div class="container mx-auto px-8">
    <div class="grid grid-cols-1 lg:grid-cols-2 gap-4 mt-8">
      <div class="flex flex-col gap-4">
        <n-card title="Connections">
          <n-card content-style="padding: 0" class="mb-4">
            <n-list size="small" hoverable>
              <n-list-item v-for="connection in connections" :key="connection.id">
                <n-thing>
                  <template #header>
                    <span class="capitalize">{{ connection.provider }}</span>
                  </template>
                  <template #description>
                    <span>{{ connection.provided_identifier }}</span>
                  </template>
                </n-thing>
              </n-list-item>
            </n-list>
          </n-card>
          <n-card content-style="padding: 0" class="mb-4">
            <n-list size="small" hoverable clickable>
              <n-list-item
                v-for="addable in connectionsAddable"
                :key="addable"
                @click="connectionAdd(addable)"
              >
                <n-thing>
                  <template #header>
                    <span class="capitalize">{{ addable }}</span>
                  </template>
                  <template #description>
                    <span>Unconnected</span>
                  </template>
                </n-thing>
              </n-list-item>
            </n-list>
          </n-card>
          <n-alert type="info" title="More actions available">
            You can open Solian and head to Account Settings to explore more actions about the
            account connections.
          </n-alert>
        </n-card>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { NCard, NList, NListItem, NThing, NAlert } from 'naive-ui'
import { computed, onMounted, ref } from 'vue'

const connectionsProviders = ['apple', 'google', 'microsoft', 'discord', 'github', 'afdian']
const connections = ref<any[]>([])

const connectionsAddable = computed(() =>
  connectionsProviders.filter((x) => connections.value.filter((e) => e.provider == x).length == 0),
)

function connectionAdd(provider: string) {
  window.open(`/api/auth/login/${provider}`, '_blank')
}

async function fetchConnections() {
  const resp = await fetch('/api/accounts/me/connections')
  connections.value = await resp.json()
}

onMounted(() => fetchConnections())
</script>
