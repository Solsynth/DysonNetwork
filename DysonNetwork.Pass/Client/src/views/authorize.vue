<template>
  <div class="flex items-center justify-center h-full p-4">
    <n-card class="w-full max-w-md" title="Authorize Application">
      <n-spin :show="isLoading">
        <div v-if="error" class="mb-4">
          <n-alert type="error" :title="error" closable @close="error = null" />
        </div>

        <!-- App Info Section -->
        <div v-if="clientInfo" class="mb-6">
          <div class="flex items-center">
            <n-avatar
              v-if="clientInfo.picture"
              :src="clientInfo.picture.url"
              :alt="clientInfo.client_name"
              size="large"
              class="mr-3"
            />
            <div>
              <h2 class="text-xl font-semibold">
                {{ clientInfo.client_name || 'Unknown Application' }}
              </h2>
              <span v-if="isNewApp">wants to access your Solar Network account</span>
              <span v-else>wants to access your account</span>
            </div>
          </div>

          <!-- Requested Permissions -->
          <n-card size="small" class="mt-4">
            <h3 class="font-medium mb-2">
              This will allow {{ clientInfo.client_name || 'the app' }} to:
            </h3>
            <ul class="space-y-1">
              <li v-for="scope in requestedScopes" :key="scope" class="flex items-start">
                <n-icon :component="CheckBoxFilled" class="mt-1 mr-2" />
                <span>{{ scope }}</span>
              </li>
            </ul>
          </n-card>

          <!-- Buttons -->
          <div class="flex gap-3 mt-4">
            <n-button
              type="primary"
              :loading="isAuthorizing"
              @click="handleAuthorize"
              class="flex-grow-1 w-1/2"
            >
              Authorize
            </n-button>
            <n-button
              type="tertiary"
              :disabled="isAuthorizing"
              @click="handleDeny"
              class="flex-grow-1 w-1/2"
            >
              Deny
            </n-button>
          </div>

          <div class="mt-4 text-xs text-gray-500 text-center">
            By authorizing, you agree to the
            <n-button text type="primary" size="tiny" @click="openTerms" class="px-1">
              Terms of Service
            </n-button>
            and
            <n-button text type="primary" size="tiny" @click="openPrivacy" class="px-1">
              Privacy Policy
            </n-button>
          </div>
        </div>
      </n-spin>
    </n-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { NCard, NButton, NSpin, NAlert, NAvatar, NIcon } from 'naive-ui'
import { CheckBoxFilled } from '@vicons/material'

const route = useRoute()

// State
const isLoading = ref(true)
const isAuthorizing = ref(false)
const error = ref<string | null>(null)
const clientInfo = ref<{
  client_name?: string
  home_uri?: string
  picture?: { url: string }
  terms_of_service_uri?: string
  privacy_policy_uri?: string
  scopes?: string[]
} | null>(null)
const isNewApp = ref(false)

// Computed properties
const requestedScopes = computed(() => {
  return clientInfo.value?.scopes || []
})

// Methods
async function fetchClientInfo() {
  try {
    const response = await fetch(`/api/auth/open/authorize?${window.location.search.slice(1)}`)
    if (!response.ok) {
      const errorData = await response.json()
      throw new Error(errorData.error_description || 'Failed to load authorization request')
    }
    clientInfo.value = await response.json()
    checkIfNewApp()
  } catch (err: any) {
    error.value = err.message || 'An error occurred while loading the authorization request'
  } finally {
    isLoading.value = false
  }
}

function checkIfNewApp() {
  // In a real app, you might want to check if this is the first time authorizing this app
  // For now, we'll just set it to false
  isNewApp.value = false
}

async function handleAuthorize() {
  isAuthorizing.value = true
  try {
    // In a real implementation, you would submit the authorization
    const response = await fetch('/api/auth/open/authorize', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        ...route.query,
        authorize: 'true',
      }),
    })

    if (!response.ok) {
      const errorData = await response.json()
      throw new Error(errorData.error_description || 'Authorization failed')
    }

    const data = await response.json()
    if (data.redirect_uri) {
      window.open(data.redirect_uri, '_self')
    }
  } catch (err: any) {
    error.value = err.message || 'An error occurred during authorization'
  } finally {
    isAuthorizing.value = false
  }
}

function handleDeny() {
  // Redirect back to the client with an error
  // Ensure redirect_uri is always a string (not an array)
  const redirectUriStr = Array.isArray(route.query.redirect_uri)
    ? route.query.redirect_uri[0] || clientInfo.value?.home_uri || '/'
    : route.query.redirect_uri || clientInfo.value?.home_uri || '/'
  const redirectUri = new URL(redirectUriStr)
  // Ensure state is always a string (not an array)
  const state = Array.isArray(route.query.state)
    ? route.query.state[0] || ''
    : route.query.state || ''
  const params = new URLSearchParams({
    error: 'access_denied',
    error_description: 'The user denied the authorization request',
    state: state,
  })
  window.open(`${redirectUri}?${params}`, "_self")
}

function openTerms() {
  window.open(clientInfo.value?.terms_of_service_uri || '#', "_blank")
}

function openPrivacy() {
  window.open(clientInfo.value?.privacy_policy_uri || '#', "_blank")
}

// Lifecycle
onMounted(() => {
  fetchClientInfo()
})
</script>

<style scoped>
/* Add any custom styles here */
</style>
